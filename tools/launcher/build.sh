#!/bin/sh
# Compiles the launcher with Mono (Linux/macOS): produces HexRevival.exe (.NET Framework 4.x, Windows), and from the
# same source with UNINSTALLER defined, HexRevival-Uninstall.exe (removes every version of the mod).
cd "$(dirname "$0")" || exit 1
mcs -target:winexe -sdk:4.5 -optimize+ -out:HexRevival.exe \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll HexRevivalLauncher.cs || exit 1
mcs -target:winexe -sdk:4.5 -optimize+ -define:UNINSTALLER -out:HexRevival-Uninstall.exe \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll HexRevivalLauncher.cs
