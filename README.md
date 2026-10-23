# HEX Revival

Play **HEX: Shards of Fate** solo, offline, now that the official servers are gone (December 2020).

HEX Revival is a mod: it does not include or modify any game file. It adds a small loader
([BepInEx](https://github.com/BepInEx/BepInEx)) and the HexRevival plugin next to `Hex.exe`,
and rebuilds locally what the server used to do: game rules, AI opponent, your collection and your decks.

## What you get

- **Start screen**: online play, or an offline account (create one: everything unlocked or earned by playing,
  tutorial, starting gold).
- **Campaign**: your campaign champions on the game's own world maps, the Howling Plains (AZ1) and
  the Alachian Sea (AZ2, reached by boat), with their quests, hubs and dialogues, the ten dungeons
  (Castle Crayburn to the Ruins of Kukatan), mercenaries, and the sea voyages of your own ship.
  Rewards follow what gameplay videos of the original game show (see [docs/PROGRESSION.md](docs/PROGRESSION.md));
  see [How faithful is the campaign?](#how-faithful-is-the-campaign) for what may differ from the original.
- **Siege**: attack other defenses for their bounty, or commit your own defense of 3 decks played by the AI.
  Until other players' defenses can be shared, the defenses to attack are configured, and the attacks on yours
  are AI-against-AI games you launch and watch (see [docs/PROGRESSION.md](docs/PROGRESSION.md#siege)).
- **Battlegrounds**: the Constructed, Sealed and Draft gauntlets (a table of 8 drafters) and the ladder,
  against AI opponents (see [docs/PROGRESSION.md](docs/PROGRESSION.md#battlegrounds)).
- **Frost Ring Arena**: the PvE boss rush (20 fights in 4 tiers) on the game's own arena screens.
- **AI opponents**: "Configure the AIs" (bottom right of the Battlegrounds, Arena and Siege screens) sets, per
  tier, which decks the AI plays (your decks, decks made for the AI, the game's AI decks or its encounters), how
  often, and their role (arena, Siege). In the deck editor, "Set Personality" also sets the deck's AI level and
  randomness, and "Modifiers" what each side starts with (health, resources, charges, cards, cards in play, shards).
  Each account has its own decks; the decks made for the AI are shared by all accounts. The settings are per
  account, with shared defaults.
- **Online play** (login screen, "Online" row): its own account and lobby (open games, challenges, chat) and
  **Host a game**: against another player in a format (Immortal, Standard, PvE, free), or against the AI. The online
  account plays the decks of every offline account (see [docs/MULTIPLAYER.md](docs/MULTIPLAYER.md)).
- **Featured Content** (main menu): tells you when a new version is out, with the link to download it. The Discord
  button of the top bar opens the [Discord server](https://discord.gg/4TWeErkhe), where players meet to talk about
  the game and find opponents.
- **Store**: boosters, the game's preconstructed decks, the Prize Exchange (Siege Sacks, Cosmic Coins),
  primal packs and battleboards, sold for gold.
- **Full collection**: every card (4 copies, unlimited basic shards) and every equipment, or a
  progression account that earns its cards by playing.
- **Deck editor** (the game's Collection screen): create, edit, delete decks; they are saved on your computer.
- The game's **tutorial** (5 short guided battles), offered when you create an account (a box to
  untick if you don't want it), left at any time from the menu (Esc), replayable from Help.
- The game's original AI, combat engine reconstructed from the client code.
- Interface in the game's language (English, French, German, Spanish, Italian, Portuguese, Polish, Russian, Turkish, Japanese, Chinese).

Not available (yet): Swiss and single-elimination tournaments.

Come and talk about the game, report bugs and find opponents on the **[Discord server](https://discord.gg/4TWeErkhe)**.

## Install and play

1. You need the **HEX: Shards of Fate game files** (Windows). The game was free-to-play, but it was
   removed from Steam at the end of 2020: Steam only installs it (app 410380) on accounts that already
   have it in their library; other accounts get "No license". If you have the game files from another
   source, the launcher lets you pick the game folder.
2. Download `HexRevival-1.1.0.zip` from the [Releases](../../releases) page and extract it anywhere.
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

Your accounts (decks, champions, collection, campaign progress) are stored in the game folder, one
folder per account in `HexRevivalSaves`.

**Uninstall**: run `HexRevival.exe --uninstall` (your decks are kept).
A Steam "Verify integrity of game files" does not remove the mod files, they are additions.

## How faithful is the campaign?

The original campaign ran on the game's servers, which are gone: the client files hold the maps,
dialogues, encounters and quest templates, but not what the server decided (rewards, dungeon rules,
some story branches). HEX Revival rebuilds these from the game's own data first, then from gameplay
videos of 2016-2018 and community guides of the time. Every value and its source is listed in
[docs/PROGRESSION.md](docs/PROGRESSION.md), where values chosen without a source are marked
**PLACEHOLDER** and information that could not be found is marked as such. In short, what may differ
from the original:

- **Rewards**: most gold, XP and item rewards were read on reward windows of videos; the rest are
  estimates (PLACEHOLDER). The contents of campaign packs are drawn at random from the right card set,
  with estimated rarity weights (AZ1 packs follow a community loot table of 2016). The starting gold and
  the starter collection of a new account are estimates.
- **The original changed over time** (2016-2018 patches): where videos disagree, the mod follows the
  most recent client, except for the ship's hull (below).
- **Not reproduced, information not found**: the optional "Challenge Completed" bonus of dungeon
  battles (+100% gold; its condition was never shown on screen), the rare extra item "Pappy Jasper's
  Whip" after some dungeons, the Kraken's Dark Chamber (+1 charge), mercenary leveling, and sea weather
  effects (never seen in any video).
- **Choices made without a source**: the ship's hull is kept from battle to battle during a voyage (the
  game's own texts and client code say so, but late-2017 videos show a full hull at every sea battle);
  dungeon bonus gold (25% from a run's second win until its first defeat) was deduced from videos;
  some story branches never seen on video (the Great Machine Graveyard when Brutecrown Bluff was not
  played, the Kraken's "Mayor Died" branch, Kukatan's Tomb of Omens) and the Sister Midnight form
  thresholds for Cunning and Instinct are estimates.
- **Dungeon boons** (Kraken library and armory, Kukatan coins and berries, the Graveyard's Mega-Bot)
  use the game's own effect cards, but how and when the server added them is reconstructed.

Corrections backed by a source (a video, a screenshot, a guide of the time) are welcome: please open
an issue.

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
| `mods/HexRevival/src/HexRevival/` | The plugin: local game server, AI driver, local profile and decks, menus |
| `mods/HexRevival.BepInEx/` | BepInEx entry point (Harmony patches) |
| `tools/launcher/` | The launcher / installer `HexRevival.exe` |
| `.github/workflows/` | Builds the launcher from source on GitHub |

## License

HEX Revival code: MIT. BepInEx: LGPL-2.1, HarmonyX / MonoMod: MIT (see `THIRD-PARTY.md`).
HEX: Shards of Fate and all its content belong to their respective owners; this project is an
unofficial, non-commercial fan project and distributes no game content.
