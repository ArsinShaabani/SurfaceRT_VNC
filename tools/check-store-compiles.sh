#!/usr/bin/env bash
#
# Type checks the Windows Store app sources without Windows.
#
# The app itself can only be *built* on Windows with the Windows 8.1 SDK, but its C# can be
# compiled anywhere against the WinRT stubs in tests/winrt-stubs.  This catches typos, wrong
# argument counts, missing members and C# language level mistakes in the app layer.
#
#   ./tools/check-store-compiles.sh
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

echo "== generating the XAML partial classes =="
python3 tools/generate-xaml-fields.py

echo
echo "== type checking the Windows Store app sources (C# 5, NETFX_CORE) =="
dotnet build tests/SurfaceVnc.StoreCompile/SurfaceVnc.StoreCompile.csproj -v q --nologo
