#!/usr/bin/env bash
#
# Builds SurfaceVnc.Core and runs the whole test suite.
#
#   ./tools/verify.sh              # build + run every test
#   ./tools/verify.sh Hextile      # only tests whose name contains "Hextile"
#
# The .NET SDK is expected either on PATH or in ./.dotnet (which is where
# tools/install-dotnet.sh puts it).  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT is needed on
# minimal Linux containers that have no ICU package installed.
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

echo "== building SurfaceVnc.Core =="
dotnet build src/SurfaceVnc.Core/SurfaceVnc.Core.csproj -v q --nologo

echo
echo "== running the test suite =="
if [ $# -gt 0 ]; then
    dotnet run --project tests/SurfaceVnc.Tests/SurfaceVnc.Tests.csproj -c Release -- "$1"
else
    dotnet run --project tests/SurfaceVnc.Tests/SurfaceVnc.Tests.csproj -c Release
fi
