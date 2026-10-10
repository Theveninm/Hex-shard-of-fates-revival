#!/bin/sh
# Compile le lanceur avec Mono (Linux/macOS) : produit HexOffline.exe (.NET Framework 4.x, Windows).
cd "$(dirname "$0")" && mcs -target:winexe -sdk:4.5 -optimize+ -out:HexOffline.exe \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll HexOfflineLauncher.cs
