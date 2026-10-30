#!/usr/bin/env bash
# Assembles the macOS release archive HexRevival-<version>-macOS.zip, to extract into the game folder (next to Hex.app):
#   official BepInEx 5.4.23.2 for macOS with run_bepinex.sh set up for Hex.app, HexRevival.dll, notices, SHA256SUMS.txt.
# The HexRevival.dll is the same as in the Windows archive (tools/package.sh).
# Usage: tools/macos/package.sh <version> <HexRevival.dll>
set -e
V="$1"; DLL="$2"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d)"; D="$WORK/HexRevival-$V-macOS"
mkdir -p "$D"
"$ROOT/tools/macos/bepinex.sh" "$D"
cp "$DLL" "$D/BepInEx/plugins/HexRevival/HexRevival.dll"
cp "$ROOT/README.md" "$ROOT/THIRD-PARTY.md" "$D/"; cp "$ROOT/LICENSE" "$D/LICENSE.txt"
(cd "$D" && find . -type f ! -name SHA256SUMS.txt | sort | xargs shasum -a 256 > SHA256SUMS.txt)
(cd "$D" && zip -qry9 "$ROOT/HexRevival-$V-macOS.zip" .)
rm -rf "$WORK"
echo "OK -> HexRevival-$V-macOS.zip"
