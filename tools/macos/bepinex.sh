#!/usr/bin/env bash
# Puts the official BepInEx 5.4.23.2 for macOS into <dir> and sets up its run_bepinex.sh for HEX (Hex.app).
# Used by install.sh (into the game folder) and package.sh (into the release archive).
# Usage: tools/macos/bepinex.sh <dir>
set -e
DIR="$1"
BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_macos_x64_5.4.23.2.zip"
if [ ! -f "$DIR/BepInEx/core/BepInEx.Preloader.dll" ] || [ ! -f "$DIR/libdoorstop.dylib" ]; then
  WORK="$(mktemp -d)"
  curl -sSL "$BEPINEX_URL" -o "$WORK/bepinex.zip"
  unzip -qo "$WORK/bepinex.zip" -d "$DIR"
  rm -f "$DIR/changelog.txt"
  rm -rf "$WORK"
fi
sed -i.orig 's/^executable_name=""/executable_name="Hex.app"/' "$DIR/run_bepinex.sh"
# Unity's Mono finds libMonoPosixHelper.dylib (which MonoMod needs to patch memory) only through DYLD_LIBRARY_PATH.
# It has to be set inside the script: macOS drops DYLD_* variables when it starts /bin/sh.
grep -q MonoEmbedRuntime "$DIR/run_bepinex.sh" || sed -i.orig '/^export DYLD_LIBRARY_PATH=/a\
export DYLD_LIBRARY_PATH="${DYLD_LIBRARY_PATH}:${BASEDIR}/Hex.app/Contents/Frameworks/MonoEmbedRuntime/osx"
' "$DIR/run_bepinex.sh"
rm -f "$DIR/run_bepinex.sh.orig"   # sed -i.orig: works with both macOS (BSD) and GNU sed
chmod +x "$DIR/run_bepinex.sh"
# The preloader fails on its first start when this folder is missing.
mkdir -p "$DIR/BepInEx/patchers" "$DIR/BepInEx/plugins/HexRevival"
