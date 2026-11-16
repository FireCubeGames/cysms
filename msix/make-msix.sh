#!/bin/bash
# Builds CYSMS.msix for Microsoft Store upload (the Store signs it).
# Usage: ./make-msix.sh "<Identity Name>" "<Publisher CN=...>" "<Publisher display name>" [version]
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
NAME=${1:?Identity Name from Partner Center}; PUB=${2:?Publisher (CN=...) from Partner Center}; PUBDISP=${3:?Publisher display name}; VER=${4:-5.0.0.0}
STAGE=$HERE/stage; rm -rf "$STAGE"; mkdir -p "$STAGE"
cp -r "$HERE/../dist/CYSMS/." "$STAGE/"
cp -r "$HERE/Assets" "$STAGE/Assets"
sed -e "s|@@IDENTITY_NAME@@|$NAME|" -e "s|@@PUBLISHER@@|$PUB|" -e "s|@@PUBLISHER_DISPLAY@@|$PUBDISP|" -e "s|@@VERSION@@|$VER|" \
    "$HERE/AppxManifest.template.xml" > "$STAGE/AppxManifest.xml"
rm -f "$HERE/CYSMS.msix"
"$MAKEMSIX" pack -d "$STAGE" -p "$HERE/CYSMS_${VER}_x64.msix"
echo "Built $HERE/CYSMS_${VER}_x64.msix"
