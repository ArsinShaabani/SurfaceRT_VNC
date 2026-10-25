#!/usr/bin/env bash
#
# Runs every check in the repository:
#
#   1. builds SurfaceVnc.Core and runs the 48 automated tests (a real RFB server over TCP)
#   2. structural checks of the Windows Store app sources (XAML, handlers, project, manifest)
#   3. type checks the Windows Store app sources with C# 5 against the WinRT stubs
#   4. regenerates the app tile/splash images
#
#   ./tools/verify-all.sh            # everything
#   ./tools/verify-all.sh Hextile    # only the tests whose name contains "Hextile"
#
set -euo pipefail

cd "$(dirname "$0")/.."

echo "### 1/5  core test suite"
./tools/verify.sh "$@"

echo
echo "### 2/5  store app structural checks"
python3 tools/check-store-app.py

echo
echo "### 3/5  store app compile check (C# 5, WinRT stubs)"
./tools/check-store-compiles.sh

echo
echo "### 4/5  cross language check (C# client vs an independent python RFB server)"
./tools/check-cross-language.sh

echo
echo "### 5/5  app assets"
python3 tools/make-assets.py

echo
echo "all checks passed"
