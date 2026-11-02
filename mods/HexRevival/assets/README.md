# Mod pictures

Embedded in `HexRevival.dll` (see `build.sh`). No game file here: pictures from the game are made at runtime.

- `discord_badge.png`: the Discord logo in a round frame like the top bar's icons, for the top bar button that opens the Discord server.
  Made from `discord_badge.svg` (`python3 -c "import cairosvg; cairosvg.svg2png(url='discord_badge.svg', write_to='discord_badge.png')"`);
  the logo's path comes from [Simple Icons](https://simpleicons.org) (CC0). The logo is Discord's trademark, used
  here to link to a Discord server as Discord's brand guidelines allow.
- `art/a0001896.png`: the alternate art of Cerulean Mirror Knight (the epic version, template c89e2e77), which the
  client's archive lacks (its bundle holds only the card's sound; see `MissingArt.cs`). Cut from the card's official
  image on the [HEX Codex](https://hex-codex.romosjr.workers.dev/cards/230-cerulean-mirror-knight/) (the alternate art
  image, 750x1050: the art box is pixels 78,100 to 671,653, trimmed top and bottom to the game's art ratio), laid out
  as a 512x512 game texture: the art in the template's portrait rectangle (left 0.07, top 0.023, right 0.92,
  bottom 0.755), the rest filled with the stretched art. Hex Entertainment's card art, as every picture the game shows.
