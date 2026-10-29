# Featured Content (main menu news)

The panels of the main menu's "Featured Content" come from the project's server, so they can be changed without a
new version of the mod.

- **Server:** `GET http://<server>:7780/news.json` and `GET http://<server>:7780/news/<picture>` (port `HEX_NEWS_PORT`,
  plain HTTP: the game's Unity 5.6 cannot be relied on for TLS). The file is `news.json` in the data volume
  (`/data/news.json` in the container); it is read on every request, so editing it is enough: no rebuild, no restart.
  When the volume has none, the default `server/HexRevival.Server/news.json` is copied there at start.
  Pictures go in `/data/news/` (png or jpg, 4 MB at most, names made of letters, digits, `.`, `-`, `_`).
- **Game:** the mod keeps the last news it received in `HexRevivalSaves/news_cache.json` (pictures in
  `HexRevivalSaves/news_pictures/`). It shows the cached panels at once and asks the server again when the cache is
  older than 30 minutes. When the server cannot be reached the cached panels stay; when it was never reached, the
  built-in panels are shown (a new version when there is one, otherwise the project page).

## Editing the news on the VPS

On the server (the commands assume the default container name):

```sh
docker exec -it hexrevival-server sh -c 'cat /data/news.json'          # see the current file
docker cp hexrevival-server:/data/news.json /tmp/news.json             # copy it out, edit /tmp/news.json, then:
docker cp /tmp/news.json hexrevival-server:/data/news.json
curl -s http://127.0.0.1:7780/news.json | head                         # check what the game receives
```
A picture: `docker cp mypicture.png hexrevival-server:/data/news/mypicture.png`, then `"image": "mypicture.png"`.

## Format

```json
{
  "items": [
    {
      "id": "release-1.2.7",
      "when": "noupdate",
      "image": "card:Phoenix Guard Messenger",
      "title": { "en": "HEX Revival 1.2.7: new loot", "fr": "HEX Revival 1.2.7 : nouveau butin" },
      "text": { "en": "...", "fr": "..." },
      "url": "https://github.com/Theveninm/Hex-shard-of-fates-revival/releases/tag/1.2.7"
    }
  ]
}
```

Panels are shown in the file's order. Fields of a panel:

| Field | Meaning |
|---|---|
| `id` | a name for you (not shown) |
| `title`, `text` | a string, or one text per language: `en`, `fr`, `de`, `es`, `it`, `pt`, `pl`, `ru`, `tr`, `ja`, `zh` (the game's language; English, then the first one, when it is missing) |
| `loc` | instead of `title`/`text`, the mod's own translated texts: `update` (a new version is available) or `project` (the project page) |
| `url` | the address the panel's "Go" button opens |
| `image` | `card:<card name>` (the card's illustration, from the game files), a file of the server's `news/` folder, or a full `http(s)://` address. Missing or not found: Argus' illustration |
| `when` | `update`: only when a newer release exists on GitHub; `noupdate`: only when this is the latest; absent: always |
| `minVersion`, `maxVersion` | only for these versions of the mod (e.g. `"maxVersion": "1.2.7"` to talk to players who have not updated) |

In `title`, `text` and `url`: `{version}` = the player's version, `{latest}` = the latest release, `{latestUrl}` = its
page, `{repo}` = the project page, `{discord}` = the Discord invite.

The newer-version check itself still asks GitHub (once every 6 hours), so a release shows up without editing the file.
