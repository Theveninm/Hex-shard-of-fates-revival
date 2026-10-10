#!/usr/bin/env bash
# Compile le module BepInEx HexRevival.dll avec Mono (mcs), contre les DLL du jeu installe.
# Les DLL du jeu ne sont pas fournies (elles appartiennent a leurs ayants droit).
#
# Usage : ./build.sh "<HEX>/Hex_Data/Managed" "<HEX>/BepInEx/core" out/HexRevival.dll
#   <HEX> = dossier du jeu, avec BepInEx 5.4.23.2 (x86) deja installe.
set -e
G="$1"; B="$2"; OUT="${3:-out/HexRevival.dll}"
if [ -z "$G" ] || [ -z "$B" ]; then
  echo "usage: $0 <Hex_Data/Managed> <BepInEx/core> [out/HexRevival.dll]"; exit 1
fi
cd "$(dirname "$0")"
S=mods/HexRevival/src/HexRevival
mkdir -p "$(dirname "$OUT")"
# -nostdlib : on compile contre le mscorlib du jeu (Unity 5.6, profil .NET 3.5).
mcs -target:library -nostdlib -noconfig -langversion:7 -debug:portable -nowarn:618 -out:"$OUT" \
 -r:"$G/mscorlib.dll" -r:"$G/System.dll" -r:"$G/System.Core.dll" -r:"$G/System.Xml.dll" \
 -r:"$G/UnityEngine.dll" -r:"$G/UnityEngine.UI.dll" -r:"$G/Assembly-CSharp.dll" -r:"$G/Assembly-CSharp-firstpass.dll" \
 -r:"$B/BepInEx.dll" -r:"$B/0Harmony.dll" \
 mods/HexRevival.BepInEx/Plugin.cs $S/*.cs
echo "OK -> $OUT"
