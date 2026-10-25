#!/usr/bin/env bash
#
# Cross language check: the real C# client against an independent RFB server written in Python.
#
# The C# test suite uses a server written in C#, so a shared misreading of RFC 6143 could hide a
# bug.  This runs the same client against a second implementation written in another language:
#
#   * the python server checks the bytes the client sends (handshake, pixel format, encodings,
#     pointer/key/clipboard events) and verifies the VNC authentication response with OpenSSL,
#   * the C# harness checks that the framebuffer it decoded matches the image the python server
#     sent (Raw, Hextile with an inherited background, coloured sub-rectangles and CopyRect).
#
#   ./tools/check-cross-language.sh
#
set -euo pipefail

cd "$(dirname "$0")/.."

if ! command -v dotnet >/dev/null 2>&1; then
    if [ -x "$PWD/.dotnet/dotnet" ]; then
        export DOTNET_ROOT="$PWD/.dotnet"
        export PATH="$DOTNET_ROOT:$PATH"
    else
        echo "dotnet not found: run tools/install-dotnet.sh first" >&2
        exit 1
    fi
fi

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

PASSWORD="secret"
PORT="$(python3 -c 'import socket; s = socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')"
SERVER_LOG="$(mktemp)"
trap 'rm -f "$SERVER_LOG"' EXIT

echo "== starting the independent python RFB server on port $PORT =="
python3 tools/crosscheck-server.py --port "$PORT" --password "$PASSWORD" > "$SERVER_LOG" 2>&1 &
SERVER_PID=$!

# Give the listener a moment to bind before the client connects.
for _ in $(seq 1 50); do
    if grep -q "listening" "$SERVER_LOG" 2>/dev/null; then break; fi
    sleep 0.1
done

echo "== running the C# client against it =="
set +e
dotnet run --project tests/SurfaceVnc.CrossCheck/SurfaceVnc.CrossCheck.csproj -c Release -- 127.0.0.1 "$PORT" "$PASSWORD"
CLIENT_EXIT=$?
wait "$SERVER_PID"
SERVER_EXIT=$?
set -e

echo
echo "== python server output =="
cat "$SERVER_LOG"

echo
echo "client exit code: $CLIENT_EXIT, server exit code: $SERVER_EXIT"
if [ "$CLIENT_EXIT" -ne 0 ] || [ "$SERVER_EXIT" -ne 0 ]; then
    echo "cross language check FAILED"
    exit 1
fi

echo "cross language check passed"
