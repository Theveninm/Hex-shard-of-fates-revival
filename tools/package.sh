#!/usr/bin/env bash
# Assembles the release archive HexRevival-<version>.zip:
#   HexRevival.exe (launcher), mod/ (official BepInEx 5.4.23.2 + HexRevival.dll), notices, SHA256SUMS.txt.
# Usage: tools/package.sh <version> <HexRevival.dll> <HexRevival.exe>
set -e
V="$1"; DLL="$2"; EXE="$3"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WORK="$(mktemp -d)"; D="$WORK/HexRevival-$V"
mkdir -p "$D/mod/BepInEx/plugins/HexRevival"
BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_win_x86_5.4.23.2.zip"
curl -sSL "$BEPINEX_URL" -o "$WORK/bepinex.zip"
unzip -q "$WORK/bepinex.zip" -d "$D/mod"
rm -f "$D/mod/changelog.txt"
cp "$DLL" "$D/mod/BepInEx/plugins/HexRevival/HexRevival.dll"
printf 'menu' > "$D/mod/HexRevival.autostart"
cp "$EXE" "$D/HexRevival.exe"
cp "$ROOT/README.md" "$ROOT/THIRD-PARTY.md" "$D/"; cp "$ROOT/LICENSE" "$D/LICENSE.txt"
(cd "$D" && find . -type f ! -name SHA256SUMS.txt | sort | xargs sha256sum > SHA256SUMS.txt)
(cd "$WORK" && zip -qr9 "$ROOT/HexRevival-$V.zip" "HexRevival-$V")
rm -rf "$WORK"
echo "OK -> HexRevival-$V.zip"
