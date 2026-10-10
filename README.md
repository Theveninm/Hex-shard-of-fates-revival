# HEX Revival

Play **HEX: Shards of Fate** solo, offline, now that the official servers are gone (December 2020).

HEX Revival is a mod: it does not include or modify any game file. It adds a small loader
([BepInEx](https://github.com/BepInEx/BepInEx)) and the HexRevival plugin next to `Hex.exe`,
and rebuilds locally what the server used to do: game rules, AI opponent, your collection and your decks.

## What you get

- **Quick Match** on the main menu: pick one of your decks, then fight
  - one of the game's ~270 campaign and dungeon encounters (with their special rules),
  - one of the game's prebuilt decks,
  - or one of your own decks played by the AI.
- **Full collection**: every card (4 copies, unlimited basic shards) and every equipment.
- **Deck editor** (the game's Collection screen): create, edit, delete decks; they are saved on your computer.
- The game's original AI, combat engine reconstructed from the client code.
- Interface in the game's language (English, French, German, Spanish, Italian, Portuguese, Polish, Russian, Turkish, Japanese, Chinese).

Not available (yet): the campaign world map, Frost Ring Arena, Siege, Battlegrounds, multiplayer.

## Install and play

1. You need the **HEX: Shards of Fate game files** (Windows). The game was free-to-play, but it was
   removed from Steam at the end of 2020: Steam only installs it (app 410380) on accounts that already
   have it in their library; other accounts get "No license". If you have the game files from another
   source, the launcher lets you pick the game folder.
2. Download `HexRevival-1.0.0.zip` from the [Releases](../../releases) page and extract it anywhere.
3. Double-click `HexRevival.exe`. It:
   - finds the game (Steam libraries, Windows registry, or the folder you chose last time),
   - otherwise offers to install it through Steam (if it is in your library) or to pick the game folder,
   - copies the files of the `mod` folder into the game folder (first launch and updates only),
   - starts the game.

### Without Steam (game files from another source)

HEX Revival works with any complete copy of the game folder; Steam itself is not used by the mod.
This project does not distribute the game files and does not link to any copy of them.

1. Put the game folder somewhere you can write without administrator rights, for example
   `C:\Games\HEX SHARDS OF FATE` (avoid `C:\Program Files`). It must contain `Hex.exe` and the
   `Hex_Data` folder, plus the DLL files that sit next to `Hex.exe`.
2. Run `HexRevival.exe`. If it does not find the game, it offers to pick the folder (if Steam is installed,
   answer No to the Steam installation question): select the folder that contains `Hex.exe`. The folder is remembered for the next launches.
   *Without the launcher:* copy the content of the `mod` folder next to `Hex.exe`, then start `Hex.exe`.
3. The mod is built for the last version of the game (December 2020). To check that your copy is that
   version, its `Hex_Data\Managed\Assembly-CSharp.dll` must have this SHA-256 checksum
   (PowerShell: `Get-FileHash "Hex_Data\Managed\Assembly-CSharp.dll"`):
   `04bdd7a93bd05cdb38c1cc2283b51f521a2e428771ef845938b6c93a203bf94d`

Files obtained outside Steam come with no guarantee: scan them with your antivirus, and be wary of any
copy that ships extra programs or asks for administrator rights.

### What the launcher does (and does not do)

Its whole source is [`tools/launcher/HexRevivalLauncher.cs`](tools/launcher/HexRevivalLauncher.cs) (about 500 lines, half of it the translated messages).

- It reads the Windows registry only to find Steam and the game, and remembers the game folder you
  picked (`HKEY_CURRENT_USER\Software\HexRevival`).
- It copies the files of the `mod` folder, as they are, into the game folder. Nothing else is written.
- It never uses the network (the only link it opens is the Steam installation of the game, when you click Yes).
- It asks for administrator rights only if Windows refuses to write into the game folder, and only after you agree.
- The `mod` folder holds the official, unmodified BepInEx 5.4.23.2 files (compare them with the
  [official release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2)) plus `HexRevival.dll`.
  `SHA256SUMS.txt` lists the checksum of every file.

Windows SmartScreen may show "Windows protected your PC" the first time, because the program is not
signed with a paid certificate: click "More info", then "Run anyway". You can also skip the launcher and
copy the content of the `mod` folder into the game folder yourself, then start the game normally.

Use `HexRevival.exe` every time you want to play (it only reinstalls when a new version is needed).

Your decks and champions are stored in the game folder (`HexRevival.decks.txt`, `HexRevival.champions.txt`).

**Uninstall**: run `HexRevival.exe --uninstall` (your decks are kept).
A Steam "Verify integrity of game files" does not remove the mod files, they are additions.

## Known limitations

- Coin flip animation may occasionally misbehave (no impact on the game).
- Some rare cards may still behave imperfectly; please open an issue with the card name.

## Building from source

- Launcher: `tools/launcher/build.sh` (Mono `mcs`) or the GitHub Actions workflow, which builds
  `HexRevival.exe` from source on GitHub's servers for every release.
- Plugin: compiled against the game's own assemblies, which cannot be redistributed. With the game
  installed, see `build.sh`.
- Release archive: `tools/package.sh <version> <HexRevival.dll> <HexRevival.exe>` downloads the official
  BepInEx release and assembles `HexRevival-<version>.zip` with its `SHA256SUMS.txt`.

### Repository layout

| Path | Content |
|---|---|
| `mods/HexRevival/src/HexRevival/` | The plugin: local game server, AI driver, local profile and decks, Quick Match menu |
| `mods/HexRevival.BepInEx/` | BepInEx entry point (Harmony patches) |
| `tools/launcher/` | The launcher / installer `HexRevival.exe` |
| `.github/workflows/` | Builds the launcher from source on GitHub |

## License

HEX Revival code: MIT. BepInEx: LGPL-2.1, HarmonyX / MonoMod: MIT (see `THIRD-PARTY.md`).
HEX: Shards of Fate and all its content belong to their respective owners; this project is an
unofficial, non-commercial fan project and distributes no game content.
