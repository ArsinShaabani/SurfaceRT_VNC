# RFB (VNC) protocol notes

This document records exactly what the client implements, so it is obvious what to expect when
connecting it to a particular VNC server.  The reference is [RFC 6143](https://www.rfc-editor.org/rfc/rfc6143).

## Version and handshake

* Speaks **RFB 3.8** (`RFB 003.008\n`) and falls back to **RFB 3.3** for old servers: the banner
  the server sends decides which handshake is used.
* Security types: `None (1)` and `VNC authentication (2)`.  If a password is configured the client
  picks VNC authentication when the server offers it, otherwise `None`.
  * VNC authentication is the classic scheme: the password is truncated to 8 bytes, every byte is
    bit reversed to form a DES key, the 16 byte challenge is bit reversed, DES-ECB encrypted, and
    the bits of the result are reversed again.  `SurfaceVnc.Core.Des` is a from-scratch DES
    implementation checked against published FIPS 46-3 vectors, and the wrapper is checked against
    a reference response produced independently with `openssl enc -des-ecb`.
* `ClientInit` always asks for a **shared** session (an existing local user keeps their desktop).
* `ServerInit` gives the desktop size, the server pixel format and the server name.

## Negotiated settings

* `SetPixelFormat` is always sent, asking for **32 bpp, depth 24, little endian, 8-8-8 true colour**
  (wire order `B,G,R,X`).  That is byte-for-byte the layout of a WinRT `WriteableBitmap`, so the
  hot path is a plain memory copy.  The alpha byte is forced to `0xFF` because `WriteableBitmap`
  interprets its buffer as *premultiplied* BGRA (a zero alpha byte would be invisible).
* Any other true colour format the server may force on us is converted through
  `PixelConverter` (8/16/24/32 bpp, either endianness, arbitrary channel shifts and maxima).
  Colour-map (paletted) formats are rejected with a clear error, because the client never asks for
  one.
* `SetEncodings` advertises, in preference order: `CopyRect (1)`, `Hextile (5)`, `CoRRE (4)`,
  `RRE (2)`, `Raw (0)`.  Servers pick the first one they support, so the picture always arrives.

## Decoders

| Encoding | Status | Notes |
|---|---|---|
| Raw (0) | yes | row by row, used as the fallback |
| CopyRect (1) | yes | overlap safe (a snapshot is taken before the copy) |
| RRE (2) | yes | 16 bit sub-rectangle geometry |
| CoRRE (4) | yes | 8 bit sub-rectangle geometry |
| Hextile (5) | yes | raw tiles, background/foreground inheritance, coloured and plain sub-rectangles |
| ZRLE (16) / Tight (7) / TRLE (15) | no | not advertised; would need zlib + JPEG plumbing |

The Hextile decoder follows the RFC byte layout exactly, including the two details that are easy
to get wrong: a coloured sub-rectangle carries its pixel value *before* the packed position byte,
and the sub-rectangle width/height bytes are the real size **minus one**.  Both rules are pinned
down by a hand written byte vector in the test suite, not only by a round trip against the test
encoder.

## Other messages

* Server → client: `FramebufferUpdate`, `SetColourMapEntries` (consumed so the stream cannot
  desynchronise), `Bell` (surfaced in the status bar) and `ServerCutText` (pushed into the Windows
  clipboard).  The `DesktopSize` pseudo encoding is honoured: the framebuffer and the view are
  resized.
* Client → server: `FramebufferUpdateRequest` (a full one after connecting, incremental ones
  afterwards, re-requested automatically after every update), `KeyEvent`, `PointerEvent`,
  `ClientCutText` (the "Paste" button).
* A single update message is decoded while holding an async gate on the framebuffer, so the UI
  thread never paints a torn frame; the UI copies the pixels out while holding the same gate and
  gives up after 250 ms instead of freezing.
* Damage rectangles of a burst of updates are merged before rendering, which keeps a busy remote
  desktop from flooding the UI thread.

## Keyboard

RFB carries **X11 keysyms**:

* ASCII and Latin-1 characters are sent as their code point (`A` → `0x41`, `é` → `0xE9`), which is
  exactly the X11 Latin-1 keysym range.
* Everything else uses the RFB "Unicode keysym" convention `0x01000000 | code point`, which is how
  Persian/Arabic/CJK text is typed into the remote machine (`ف` → `0x01000641`).
* Keys without a character (arrows, F keys, Esc, Tab, Ctrl/Alt/Shift/Win, ...) are mapped from the
  Windows virtual key code by `RemoteKeys.FromVirtualKey`.

## Input mapping

`ViewportMapper` owns the whole touch mapping:

* `Fit` scales the desktop so it fits (never upscaling more than 4x) and centres it;
* `ActualSize` is 1:1 and can be panned, with the offsets clamped so the desktop cannot be dragged
  off screen;
* a touch outside the remote desktop is rejected instead of being clamped to the nearest pixel;
* panning is expressed in view coordinates and converted back to remote pixels, so a click always
  lands on the pixel under the finger.

## Not implemented (on purpose)

* RDP — a different, far larger protocol; see the README for why VNC was chosen.
* Tight/ZRLE/TRLE compression, JPEG quality settings, file transfer, `ContinuousUpdates`,
  `ClientFence`, keyboard LED synchronisation, multiple monitors.
* Colour-map (8 bpp paletted) framebuffers.

## Tests

`tools/verify.sh` builds the core and runs the suite in `tests/SurfaceVnc.Tests`.  The suite
contains a real RFB server (a separate implementation of the server side, written from the RFC)
which the production client is driven against over TCP on `127.0.0.1`, so handshakes, encodings,
input events and error paths are all exercised for real rather than mocked.

`tools/check-cross-language.sh` adds a second, independent check: the same client is driven against
an RFB server written in **Python** from RFC 6143 (`tools/crosscheck-server.py`).  That server
verifies the bytes the client sends (pixel format, encoding order, pointer/key/clipboard events) and
checks the VNC authentication response with **OpenSSL**, while the C# harness verifies the decoded
framebuffer against the image the Python server sent.  Two independent implementations in two
languages agreeing on the wire format is what makes a shared misreading of the spec unlikely to go
unnoticed.
