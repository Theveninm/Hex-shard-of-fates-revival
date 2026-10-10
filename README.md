# HEX Offline

Play **HEX: Shards of Fate** solo, offline, now that the official servers are gone (December 2020).
*Version française plus bas.*

HEX Offline is a mod: it does not include or modify any game file. It adds a small loader
([BepInEx](https://github.com/BepInEx/BepInEx)) and the HexOffline plugin next to `Hex.exe`,
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

1. You need **HEX: Shards of Fate installed through Steam** (Windows). The game was removed from the
   Steam store at the end of 2020: it must already be in your Steam library.
2. Download `HexOffline-1.0.0.zip` from the [Releases](../../releases) page and extract it anywhere.
3. Double-click `HexOffline.exe`. It:
   - finds the game in your Steam libraries (or asks you for the folder),
   - offers to install the game through Steam, or Steam itself, if something is missing,
   - copies the files of the `mod` folder into the game folder (first launch and updates only),
   - starts the game.

### What the launcher does (and does not do)

Its whole source is [`tools/launcher/HexOfflineLauncher.cs`](tools/launcher/HexOfflineLauncher.cs) (about 300 lines).

- It reads the Windows registry only to find where Steam is installed.
- It copies the files of the `mod` folder, as they are, into the game folder. Nothing else is written.
- It never uses the network (the only links it opens are Steam / the Steam download page, in your browser, when you click Yes).
- It asks for administrator rights only if Windows refuses to write into the game folder, and only after you agree.
- The `mod` folder holds the official, unmodified BepInEx 5.4.23.2 files (compare them with the
  [official release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.2)) plus `HexOffline.dll`.
  `SHA256SUMS.txt` lists the checksum of every file.

Windows SmartScreen may show "Windows protected your PC" the first time, because the program is not
signed with a paid certificate: click "More info", then "Run anyway". You can also skip the launcher and
copy the content of the `mod` folder into the game folder yourself, then start the game normally.

Use `HexOffline.exe` every time you want to play (it only reinstalls when a new version is needed).

Your decks and champions are stored in the game folder (`HexOffline.decks.txt`, `HexOffline.champions.txt`).

**Uninstall**: run `HexOffline.exe --uninstall` (your decks are kept).
A Steam "Verify integrity of game files" does not remove the mod files, they are additions.

## Known limitations

- Coin flip animation may occasionally misbehave (no impact on the game).
- Some rare cards may still behave imperfectly; please open an issue with the card name.

## Building from source

- Launcher: `tools/launcher/build.sh` (Mono `mcs`) or the GitHub Actions workflow, which builds
  `HexOffline.exe` from source on GitHub's servers for every release.
- Plugin: compiled against the game's own assemblies, which cannot be redistributed. With the game
  installed, see `build.sh`.
- Release archive: `tools/package.sh <version> <HexOffline.dll> <HexOffline.exe>` downloads the official
  BepInEx release and assembles `HexOffline-<version>.zip` with its `SHA256SUMS.txt`.

### Repository layout

| Path | Content |
|---|---|
| `mods/HexOffline/src/HexOffline/` | The plugin: local game server, AI driver, local profile and decks, Quick Match menu |
| `mods/HexOffline.BepInEx/` | BepInEx entry point (Harmony patches) |
| `tools/launcher/` | The launcher / installer `HexOffline.exe` |
| `.github/workflows/` | Builds the launcher from source on GitHub |

## License

HEX Offline code: MIT. BepInEx: LGPL-2.1, HarmonyX / MonoMod: MIT (see `THIRD-PARTY.md`).
HEX: Shards of Fate and all its content belong to their respective owners; this project is an
unofficial, non-commercial fan project and distributes no game content.

---

# HEX Offline (français)

Jouer à **HEX: Shards of Fate** en solo, hors ligne, depuis la fermeture des serveurs officiels (décembre 2020).

HEX Offline est un mod : il ne contient ni ne modifie aucun fichier du jeu. Il ajoute un petit chargeur
(BepInEx) et le module HexOffline à côté de `Hex.exe`, et reconstruit sur votre ordinateur ce que faisait
le serveur : règles du jeu, adversaire IA, collection et decks.

## Contenu

- **Partie rapide** dans le menu principal : choisissez un de vos decks, puis affrontez
  - une des ~270 rencontres de la campagne et des donjons (avec leurs règles spéciales),
  - un des decks préconstruits du jeu,
  - ou un de vos propres decks joué par l'IA.
- **Collection complète** : toutes les cartes (4 exemplaires, shards de base illimités) et tous les équipements.
- **Éditeur de deck** (écran Collection du jeu) : créer, modifier, supprimer ; les decks sont enregistrés sur votre ordinateur.
- L'IA d'origine du jeu, moteur de combat reconstruit à partir du code du client.
- Interface dans la langue du jeu.

Pas (encore) disponible : la carte du monde de la campagne, Frost Ring Arena, Siege, Battlegrounds, le multijoueur.

## Installer et jouer

1. Il faut **HEX: Shards of Fate installé avec Steam** (Windows). Le jeu a été retiré de la boutique Steam
   fin 2020 : il doit déjà être dans votre bibliothèque.
2. Téléchargez `HexOffline-1.0.0.zip` depuis la page [Releases](../../releases) et extrayez-le où vous voulez.
3. Double-cliquez sur `HexOffline.exe`. Il :
   - trouve le jeu dans vos bibliothèques Steam (ou vous demande le dossier),
   - propose d'installer le jeu avec Steam, ou Steam lui-même, s'il manque quelque chose,
   - copie les fichiers du dossier `mod` dans le dossier du jeu (au premier lancement et aux mises à jour),
   - lance le jeu.

### Ce que fait le lanceur (et ce qu'il ne fait pas)

Tout son code source est [`tools/launcher/HexOfflineLauncher.cs`](tools/launcher/HexOfflineLauncher.cs) (environ 300 lignes).

- Il lit le registre Windows uniquement pour savoir où Steam est installé.
- Il copie tels quels les fichiers du dossier `mod` dans le dossier du jeu. Rien d'autre n'est écrit.
- Il n'utilise jamais le réseau (les seuls liens qu'il ouvre, dans votre navigateur et si vous cliquez Oui, sont Steam et sa page de téléchargement).
- Il ne demande les droits administrateur que si Windows refuse l'écriture dans le dossier du jeu, et seulement avec votre accord.
- Le dossier `mod` contient les fichiers officiels, non modifiés, de BepInEx 5.4.23.2 et `HexOffline.dll`.
  `SHA256SUMS.txt` donne l'empreinte de chaque fichier.

Windows SmartScreen peut afficher « Windows a protégé votre ordinateur » au premier lancement, car le
programme n'est pas signé avec un certificat payant : cliquez « Informations complémentaires », puis
« Exécuter quand même ». Vous pouvez aussi vous passer du lanceur : copiez le contenu du dossier `mod`
dans le dossier du jeu, puis lancez le jeu normalement.

Utilisez `HexOffline.exe` à chaque fois que vous voulez jouer.

**Désinstaller** : lancez `HexOffline.exe --uninstall` (vos decks sont conservés).
