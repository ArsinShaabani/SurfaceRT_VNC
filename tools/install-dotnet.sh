#!/usr/bin/env bash
#
# Installs a private copy of the .NET SDK into ./.dotnet so that the test suite can be built and
# run without touching the rest of the machine (no root, no package manager needed).
#
#   tools/install-dotnet.sh            # installs the latest 8.0 SDK into ./.dotnet
#   DOTNET_CHANNEL=9.0 tools/install-dotnet.sh
#
set -euo pipefail

cd "$(dirname "$0")/.."

CHANNEL="${DOTNET_CHANNEL:-8.0}"
INSTALL_DIR="$PWD/.dotnet"
SCRIPT="$PWD/.tools/dotnet-install.sh"

mkdir -p "$PWD/.tools"

if [ ! -x "$SCRIPT" ]; then
    echo "downloading dotnet-install.sh ..."
    curl -sSL https://dot.net/v1/dotnet-install.sh -o "$SCRIPT"
    chmod +x "$SCRIPT"
fi

echo "installing the .NET $CHANNEL SDK into $INSTALL_DIR ..."
"$SCRIPT" --channel "$CHANNEL" --install-dir "$INSTALL_DIR" --no-path

echo
echo "done.  tools/verify.sh will pick it up automatically."
echo "to use it in your own shell:  export DOTNET_ROOT=\"$INSTALL_DIR\"; export PATH=\"$INSTALL_DIR:\$PATH\""
