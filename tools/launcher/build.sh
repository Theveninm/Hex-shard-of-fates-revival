#!/bin/sh
# Compiles the launcher with Mono (Linux/macOS): produces HexRevival.exe (.NET Framework 4.x, Windows).
cd "$(dirname "$0")" && mcs -target:winexe -sdk:4.5 -optimize+ -out:HexRevival.exe \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll HexRevivalLauncher.cs
