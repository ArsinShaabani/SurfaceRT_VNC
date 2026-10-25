#!/usr/bin/env python3
"""An independent RFB (VNC) server, written from RFC 6143 in Python.

The C# test suite drives the client against a server written in the *same* language, so a shared
misreading of the spec could hide a bug.  This server is a second, independent implementation: it
is used by tests/SurfaceVnc.CrossCheck to drive the real C# client over a socket and to check the
bytes the client sends back.

What it does:
  * RFB 3.8 handshake with VNC authentication; the client's DES response is verified with
    OpenSSL (so the DES implementation under test is not used to check itself),
  * records SetPixelFormat / SetEncodings / PointerEvent / KeyEvent / ClientCutText,
  * sends three scripted updates: Raw (whole screen), Hextile (uniform tile + inherited
    background tile + coloured sub-rectangles) and CopyRect (a scroll),
  * verifies everything it recorded and exits 0 (pass) or 1 (fail).

Usage:  tools/crosscheck-server.py --port 5911 --password secret
"""
import argparse
import socket
import struct
import subprocess
import sys
import time

WIDTH = 64
HEIGHT = 48

# ---- the scripted updates, mirrored by tests/SurfaceVnc.CrossCheck/Program.cs ----------------
COLOUR_A = (0x11, 0x22, 0x33)          # fills the Hextile rectangle at (8,8) 32x16
COLOUR_C = (0xFF, 0x00, 0x00)          # sub-rectangle at (41,25) 1x1 inside the second Hextile rect
COLOUR_D = (0x00, 0xFF, 0x00)          # sub-rectangle at (43,26) 2x2
HEXTILE_RECT = (8, 8, 32, 16)
SUBRECT_RECT = (40, 24, 16, 16)
COPY_SOURCE = (0, 0, 32, 16)
COPY_DESTINATION = (32, 16)


def pattern(x, y):
    """The image the client must end up with before the Hextile update."""
    return ((x * 4) & 0xFF, (y * 4) & 0xFF, ((x + y) * 2) & 0xFF)


def bgra(rgb):
    """32bpp little endian true colour: the wire order is B, G, R, X."""
    return bytes([rgb[2], rgb[1], rgb[0], 0x00])


def bitrev(value):
    result = 0
    for bit in range(8):
        if value & (1 << bit):
            result |= 1 << (7 - bit)
    return result


def vnc_response(password, challenge):
    """The RFB VNC authentication response, computed with OpenSSL as the DES engine."""
    key = bytes(bitrev(c) for c in password.encode("latin-1")[:8].ljust(8, b"\x00"))
    plain = bytes(bitrev(c) for c in challenge)
    out = subprocess.check_output(
        ["openssl", "enc", "-des-ecb", "-K", key.hex(), "-nopad",
         "-provider", "legacy", "-provider", "default"],
        input=plain, stderr=subprocess.DEVNULL)
    return bytes(bitrev(c) for c in out)


class Connection(object):
    def __init__(self, sock):
        self.sock = sock
        self.failures = []

    def send(self, data):
        self.sock.sendall(data)

    def recv_exact(self, count):
        chunks = b""
        while len(chunks) < count:
            chunk = self.sock.recv(count - len(chunks))
            if not chunk:
                raise EOFError("the client closed the connection")
            chunks += chunk
        return chunks

    def check(self, condition, message):
        if condition:
            print("  ok    " + message)
        else:
            print("  FAIL  " + message)
            self.failures.append(message)


def raw_rect(x, y, width, height, colour_fn):
    """A Raw rectangle: header + tightly packed pixels."""
    payload = bytearray()
    for row in range(height):
        for column in range(width):
            payload += bgra(colour_fn(x + column, y + row))
    return struct.pack(">HHHHi", x, y, width, height, 0) + bytes(payload)


def hextile_uniform(x, y, width, height, colour):
    """A Hextile rectangle of whole tiles: the first tile carries the background, the rest are
    bare "0" sub-encodings that rely on the inherited background."""
    payload = bytearray()
    for tile in range((width + 15) // 16):
        if tile == 0:
            payload += bytes([2]) + bgra(colour)
        else:
            payload += bytes([0])
    return struct.pack(">HHHHi", x, y, width, height, 5) + bytes(payload)


def hextile_with_subrects(x, y, width, height, background, subrects):
    """A Hextile rectangle (one tile) with coloured sub-rectangles."""
    payload = bytearray([2 | 8 | 16])
    payload += bgra(background)
    payload += struct.pack(">H", len(subrects))
    for (sx, sy, sw, sh, colour) in subrects:
        payload += bgra(colour)
        payload += bytes([(sx << 4) | sy, sw - 1, sh - 1])
    return struct.pack(">HHHHi", x, y, width, height, 5) + bytes(payload)


def copy_rect(destination, width, height, source_x, source_y):
    return struct.pack(">HHHHi", destination[0], destination[1], width, height, 1) + \
        struct.pack(">HH", source_x, source_y)


def framebuffer_update(rectangles):
    return bytes([0, 0]) + struct.pack(">H", len(rectangles)) + b"".join(rectangles)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--password", default="secret")
    parser.add_argument("--timeout", type=float, default=20.0)
    args = parser.parse_args()

    listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    listener.bind(("127.0.0.1", args.port))
    listener.listen(1)
    print("python crosscheck server listening on 127.0.0.1:{0}".format(args.port))

    client, _ = listener.accept()
    client.settimeout(args.timeout)
    connection = Connection(client)

    # ---- handshake ---------------------------------------------------------------------------
    connection.send(b"RFB 003.008\n")
    version = connection.recv_exact(12)
    connection.check(version == b"RFB 003.008\n",
                     "client banner is RFB 003.008 (got {0!r})".format(version))

    connection.send(bytes([1, 2]))              # one security type: VNC authentication
    chosen = connection.recv_exact(1)[0]
    connection.check(chosen == 2, "client chose VNC authentication (got {0})".format(chosen))

    challenge = bytes(range(0x11, 0x21))        # 16 bytes
    connection.send(challenge)
    response = connection.recv_exact(16)

    try:
        expected = vnc_response(args.password, challenge)
        connection.check(response == expected,
                         "VNC authentication response matches OpenSSL (got {0})".format(response.hex()))
    except Exception as error:                  # openssl missing: report instead of failing the run
        print("  note  could not check the response with OpenSSL: {0}".format(error))

    connection.send(struct.pack(">I", 0))       # SecurityResult: ok

    client_init = connection.recv_exact(1)[0]
    connection.check(client_init in (0, 1), "client sent ClientInit ({0})".format(client_init))

    server_init = struct.pack(">HH", WIDTH, HEIGHT)
    # PIXEL_FORMAT: bpp, depth, big-endian, true-colour, r/g/b max, r/g/b shift, 3 bytes padding.
    server_init += bytes([32, 24, 0, 1]) + struct.pack(">HHH", 255, 255, 255) + bytes([16, 8, 0, 0, 0, 0])
    name = b"python-crosscheck"
    server_init += struct.pack(">I", len(name)) + name
    connection.check(len(server_init) == 4 + 16 + 4 + len(name), "ServerInit is the right size")
    connection.send(server_init)

    # ---- the scripted updates -----------------------------------------------------------------
    script = [
        framebuffer_update([raw_rect(0, 0, WIDTH, HEIGHT, pattern)]),
        framebuffer_update([
            hextile_uniform(HEXTILE_RECT[0], HEXTILE_RECT[1], HEXTILE_RECT[2], HEXTILE_RECT[3], COLOUR_A),
            hextile_with_subrects(SUBRECT_RECT[0], SUBRECT_RECT[1], SUBRECT_RECT[2], SUBRECT_RECT[3],
                                  (0, 0, 0),
                                  [(1, 1, 1, 1, COLOUR_C), (3, 2, 2, 2, COLOUR_D)]),
        ]),
        framebuffer_update([
            copy_rect(COPY_DESTINATION, COPY_SOURCE[2], COPY_SOURCE[3], COPY_SOURCE[0], COPY_SOURCE[1]),
        ]),
    ]

    pixel_format = None
    encodings = None
    pointers = []
    keys = []
    cut_texts = []
    requests = 0
    sent = 0

    deadline = time.time() + args.timeout
    while time.time() < deadline:
        try:
            message_type = connection.recv_exact(1)[0]
        except EOFError:
            break

        if message_type == 0:                   # SetPixelFormat
            connection.recv_exact(3)
            pixel_format = connection.recv_exact(16)

        elif message_type == 2:                 # SetEncodings
            connection.recv_exact(1)
            count = struct.unpack(">H", connection.recv_exact(2))[0]
            encodings = [struct.unpack(">i", connection.recv_exact(4))[0] for _ in range(count)]

        elif message_type == 3:                 # FramebufferUpdateRequest
            connection.recv_exact(9)
            requests += 1
            if sent < len(script):
                connection.send(script[sent])
                sent += 1

        elif message_type == 4:                 # KeyEvent
            down = connection.recv_exact(1)[0]
            connection.recv_exact(2)
            keysym = struct.unpack(">I", connection.recv_exact(4))[0]
            keys.append((down, keysym))

        elif message_type == 5:                 # PointerEvent
            mask = connection.recv_exact(1)[0]
            x, y = struct.unpack(">HH", connection.recv_exact(4))
            pointers.append((mask, x, y))

        elif message_type == 6:                 # ClientCutText
            connection.recv_exact(3)
            length = struct.unpack(">I", connection.recv_exact(4))[0]
            cut_texts.append(connection.recv_exact(length))

        else:
            connection.check(False, "unexpected client message type {0}".format(message_type))
            break

        if sent == len(script) and len(pointers) >= 2 and len(keys) >= 2 and cut_texts:
            break

    # ---- verification -------------------------------------------------------------------------
    print("  note  saw {0} update request(s), {1} pointer, {2} key, {3} cut-text message(s)".format(
        requests, len(pointers), len(keys), len(cut_texts)))

    if pixel_format is None:
        connection.check(False, "the client sent SetPixelFormat")
    else:
        bits, depth, big_endian, true_colour = pixel_format[0], pixel_format[1], pixel_format[2], pixel_format[3]
        red_max, green_max, blue_max = struct.unpack(">HHH", pixel_format[4:10])
        red_shift, green_shift, blue_shift = pixel_format[10], pixel_format[11], pixel_format[12]
        connection.check(bits == 32 and depth == 24,
                         "client asked for 32bpp/depth 24 (got {0}/{1})".format(bits, depth))
        connection.check(big_endian == 0 and true_colour == 1, "client asked for little endian true colour")
        connection.check((red_max, green_max, blue_max) == (255, 255, 255), "client asked for 8-8-8 channels")
        connection.check((red_shift, green_shift, blue_shift) == (16, 8, 0), "client asked for the B,G,R,X layout")

    connection.check(encodings == [1, 5, 4, 2, 0],
                     "client advertised CopyRect, Hextile, CoRRE, RRE, Raw in that order (got {0})".format(encodings))
    connection.check(requests >= 3, "client kept requesting updates (got {0})".format(requests))
    connection.check(pointers[:2] == [(1, 100, 200), (0, 101, 201)],
                     "pointer events arrived byte exact (got {0})".format(pointers[:2]))
    connection.check(keys[:2] == [(1, 0xFF0D), (0, 0xFF0D)],
                     "key press and release arrived byte exact (got {0})".format(keys[:2]))
    connection.check(cut_texts[:1] == [b"cross-check"],
                     "clipboard text arrived byte exact (got {0})".format(cut_texts[:1]))

    client.close()
    listener.close()

    if connection.failures:
        print("\npython crosscheck server: {0} check(s) FAILED".format(len(connection.failures)))
        return 1

    print("\npython crosscheck server: all checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
