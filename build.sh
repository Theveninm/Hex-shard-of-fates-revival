#!/usr/bin/env bash
# Compiles the BepInEx plugin HexRevival.dll with Mono (mcs), against the installed game's DLLs.
# The game DLLs are not provided (they belong to their rights holders).
#
# Usage: ./build.sh "<HEX>/Hex_Data/Managed" "<HEX>/BepInEx/core" out/HexRevival.dll
#   <HEX> = game folder, with BepInEx 5.4.23.2 (x86) already installed.
set -e
G="$1"; B="$2"; OUT="${3:-out/HexRevival.dll}"
if [ -z "$G" ] || [ -z "$B" ]; then
  echo "usage: $0 <Hex_Data/Managed> <BepInEx/core> [out/HexRevival.dll]"; exit 1
fi
cd "$(dirname "$0")"
S=mods/HexRevival/src/HexRevival
mkdir -p "$(dirname "$OUT")"
# -nostdlib: compiles against the game's mscorlib (Unity 5.6, .NET 3.5 profile).
mcs -target:library -nostdlib -noconfig -langversion:7 -debug:portable -nowarn:618 -out:"$OUT" \
 -r:"$G/mscorlib.dll" -r:"$G/System.dll" -r:"$G/System.Core.dll" -r:"$G/System.Xml.dll" \
 -r:"$G/UnityEngine.dll" -r:"$G/UnityEngine.UI.dll" -r:"$G/Assembly-CSharp.dll" -r:"$G/Assembly-CSharp-firstpass.dll" \
 -r:"$B/BepInEx.dll" -r:"$B/0Harmony.dll" \
 -resource:mods/HexRevival/assets/discord_badge.png,HexRevival.discord_badge.png \
 -resource:mods/HexRevival/assets/tournament_decks.txt,HexRevival.tournament_decks.txt \
 mods/HexRevival.BepInEx/Plugin.cs $S/*.cs
echo "OK -> $OUT"
