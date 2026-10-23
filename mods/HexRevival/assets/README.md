# Mod pictures

Embedded in `HexRevival.dll` (see `build.sh`). No game file here: pictures from the game are made at runtime.

- `discord_badge.png`: the Discord logo in a round frame like the top bar's icons, for the top bar button that opens the Discord server.
  Made from `discord_badge.svg` (`python3 -c "import cairosvg; cairosvg.svg2png(url='discord_badge.svg', write_to='discord_badge.png')"`);
  the logo's path comes from [Simple Icons](https://simpleicons.org) (CC0). The logo is Discord's trademark, used
  here to link to a Discord server as Discord's brand guidelines allow.
