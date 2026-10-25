#!/usr/bin/env python3
"""Generates the tile/splash PNGs referenced by Package.appxmanifest.

The app needs real image files (StoreLogo, Square150x150, Square30x30, Wide310x150 and the
620x300 splash screen).  Rather than shipping opaque binaries, this script draws them: a dark
blue tile with a light blue "screen" rectangle and a stylised VNC eye.  Pure standard library
(zlib + struct), no Pillow needed.

Usage:  python3 tools/make-assets.py
"""
import os
import struct
import zlib

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "SurfaceVnc.Store", "Assets")

BACKGROUND = (0x10, 0x2A, 0x43)
SCREEN = (0x1B, 0x3A, 0x5C)
ACCENT = (0x3F, 0xA7, 0xD6)
GLOW = (0xB8, 0xC7, 0xD6)


def write_png(path, width, height, pixel_fn):
    """Writes an RGBA PNG, calling pixel_fn(x, y) -> (r, g, b, a)."""
    raw = bytearray()
    for y in range(height):
        raw.append(0)  # filter type 0 (None) for every scan line
        for x in range(width):
            raw.extend(pixel_fn(x, y))

    def chunk(tag, payload):
        data = tag + payload
        return struct.pack(">I", len(payload)) + data + struct.pack(">I", zlib.crc32(data) & 0xFFFFFFFF)

    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", header)
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")

    with open(path, "wb") as handle:
        handle.write(png)


def make_tile(width, height, eye_scale=1.0):
    """Dark tile with a lighter rounded screen and an accent coloured 'eye' in the middle."""
    margin_x = max(2, int(width * 0.12))
    margin_y = max(2, int(height * 0.12))
    inner_left, inner_right = margin_x, width - margin_x
    inner_top, inner_bottom = margin_y, height - margin_y

    centre_x = width / 2.0
    centre_y = height / 2.0
    radius = min(width, height) * 0.22 * eye_scale
    pupil = radius * 0.42

    def pixel(x, y):
        inside_screen = inner_left <= x < inner_right and inner_top <= y < inner_bottom
        dx = x + 0.5 - centre_x
        dy = y + 0.5 - centre_y
        distance = (dx * dx + dy * dy) ** 0.5

        if distance <= pupil:
            return (ACCENT[0], ACCENT[1], ACCENT[2], 255)
        if distance <= radius:
            # Eye ring: a thin accent circle with the screen colour inside it.
            if distance >= radius - max(1.0, radius * 0.16):
                return (ACCENT[0], ACCENT[1], ACCENT[2], 255)
            return (GLOW[0], GLOW[1], GLOW[2], 255)
        if inside_screen:
            return (SCREEN[0], SCREEN[1], SCREEN[2], 255)
        return (BACKGROUND[0], BACKGROUND[1], BACKGROUND[2], 255)

    return pixel


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    tiles = [
        ("StoreLogo.png", 50, 50, 1.0),
        ("SmallLogo.png", 30, 30, 1.0),
        ("Logo.png", 150, 150, 1.0),
        ("WideLogo.png", 310, 150, 0.8),
        ("SplashScreen.png", 620, 300, 0.7),
    ]

    for name, width, height, eye_scale in tiles:
        path = os.path.join(OUT_DIR, name)
        write_png(path, width, height, make_tile(width, height, eye_scale))
        print("wrote {0} ({1}x{2}, {3} bytes)".format(path, width, height, os.path.getsize(path)))


if __name__ == "__main__":
    main()
