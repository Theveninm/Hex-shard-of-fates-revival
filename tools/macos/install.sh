#!/usr/bin/env bash
# Installs HEX Revival into the native macOS version of HEX (Hex.app): BepInEx for macOS + HexRevival.dll.
# The same HexRevival.dll runs on Windows and macOS (LocalServer.Root finds the folder next to Hex.app).
# Start the game with run_bepinex.sh, or from Steam with the launch options: "<game dir>/run_bepinex.sh" %command%
# Usage: tools/macos/install.sh <HexRevival.dll> [game dir]
set -e
DLL="$1"
GAME="${2:-$HOME/Library/Application Support/Steam/steamapps/common/HEX SHARDS OF FATE}"
[ -f "$DLL" ] || { echo "HexRevival.dll not found: $DLL"; exit 1; }
[ -d "$GAME/Hex.app" ] || { echo "Hex.app not found in: $GAME"; exit 1; }
"$(dirname "$0")/bepinex.sh" "$GAME"
cp "$DLL" "$GAME/BepInEx/plugins/HexRevival/HexRevival.dll.new"
mv "$GAME/BepInEx/plugins/HexRevival/HexRevival.dll.new" "$GAME/BepInEx/plugins/HexRevival/HexRevival.dll"
echo "OK -> $GAME (start: \"$GAME/run_bepinex.sh\")"
