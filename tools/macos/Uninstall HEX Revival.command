#!/usr/bin/env bash
# Removes HEX Revival (any version, including its former name HEX Offline) from the macOS game folder: double-click it
# in the game folder (next to Hex.app), or run it with the game folder as argument. The game's own files are not
# touched; the player's saves are kept unless they choose to delete them.
GAME="${1:-$(cd "$(dirname "$0")" && pwd)}"
ask() { osascript -e "button returned of (display dialog \"$1\" with title \"HEX Revival\" buttons {\"$2\", \"$3\"} default button \"$4\")" 2>/dev/null; }
say() { osascript -e "display dialog \"$1\" with title \"HEX Revival\" buttons {\"OK\"} default button \"OK\"" >/dev/null 2>&1; echo "$1"; }
if [ ! -d "$GAME/Hex.app" ]; then say "Hex.app is not in this folder: $GAME. Put this file in the game folder (next to Hex.app), then open it again."; exit 1; fi
[ "$(ask "Remove HEX Revival (any version) from: $GAME? The game folder goes back to the original game." Cancel Remove Remove)" = "Remove" ] || exit 0
while pgrep -f "$GAME/Hex.app/Contents/MacOS" >/dev/null 2>&1; do
  [ "$(ask "HEX is running. Close the game, then click OK." Cancel OK OK)" = "OK" ] || exit 0
done
SAVES=$(ask "Also delete your HEX Revival saves (accounts, decks, champions, collection)? No: they are kept in the game folder, and come back if you reinstall the mod." Yes No No)
cd "$GAME" || exit 1
rm -rf BepInEx/plugins/HexRevival BepInEx/plugins/HexOffline
# BepInEx and its loader only when no other mod uses them.
if [ -z "$(ls -A BepInEx/plugins 2>/dev/null)" ]; then
  rm -rf BepInEx libdoorstop.dylib run_bepinex.sh .doorstop_version doorstop_config.ini
fi
for f in HexRevival* HexOffline*; do
  [ -e "$f" ] || continue
  if [ "$SAVES" != "Yes" ]; then
    case "$f" in HexRevivalSaves*|HexRevivalOnline*|HexOffline.*.txt) continue ;; esac
  fi
  rm -rf "$f"
done
say "HEX Revival has been removed. If you set Steam's launch options for it (run_bepinex.sh %command%), clear them: right-click HEX > Properties > Launch options."
