#!/usr/bin/env bash
# Removes HEX Revival (any version, including its former name HEX Offline) from the macOS game folder: double-click it
# in the game folder (next to Hex.app), or run it with the game folder as argument. The game's own files are not
# touched; the player's saves are kept unless they choose to delete them. HEX's Steam launch options are cleared too.
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
# Steam's launch options for HEX ("<game>/run_bepinex.sh" %command%, set when installing): cleared in each Steam
# account's localconfig.vdf, only in HEX's own entry (app 410380) and only if they point to run_bepinex.sh. Steam
# rewrites that file when it quits, so it has to be closed first.
STEAM="$HOME/Library/Application Support/Steam/userdata"
CONFIGS=$(grep -l 'run_bepinex\.sh' "$STEAM"/*/config/localconfig.vdf 2>/dev/null)
if [ -n "$CONFIGS" ]; then
  if pgrep -x steam_osx >/dev/null 2>&1; then
    if [ "$(ask "Steam has to be closed to clear HEX's launch options." Skip "Quit Steam" "Quit Steam")" = "Quit Steam" ]; then
      osascript -e 'tell application "Steam" to quit' >/dev/null 2>&1
      for i in $(seq 1 30); do pgrep -x steam_osx >/dev/null 2>&1 || break; sleep 1; done
    fi
  fi
  if pgrep -x steam_osx >/dev/null 2>&1; then
    LEFT="Steam is still open: clear HEX's launch options yourself (right-click HEX > Properties > Launch options)."
  else
    echo "$CONFIGS" | while IFS= read -r cfg; do
      awk -v id='"410380"' '
        { t = $0; gsub(/^[ \t]+|[ \t]+$/, "", t) }
        depth == 0 && t == id { pending = 1; print; next }
        pending && t == "{" { depth = 1; pending = 0; print; next }
        { pending = 0 }
        depth > 0 && t == "{" { depth++ }
        depth > 0 && t == "}" { depth-- }
        depth == 1 && t ~ /^"LaunchOptions"/ && t ~ /run_bepinex\.sh/ { sub(/"LaunchOptions".*/, "\"LaunchOptions\"\t\t\"\""); }
        { print }' "$cfg" > "$cfg.hexrevival" && mv "$cfg.hexrevival" "$cfg"
    done
  fi
fi
say "HEX Revival has been removed.${LEFT:+ $LEFT}"
