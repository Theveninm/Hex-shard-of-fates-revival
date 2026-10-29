# Notes for agents working on HEX Revival

## Releasing a new version (every step, in this order)

1. **Version number:** bump `Community.Version` (`mods/HexRevival/src/HexRevival/Community.cs`) and the launcher's
   `Version` and `AssemblyVersion` / `AssemblyFileVersion` (`tools/launcher/HexRevivalLauncher.cs`), in a pull request
   merged into `main` before building.
2. **Archive:** build `HexRevival.dll` (`build.sh`) and `HexRevival.exe` (`tools/launcher/build.sh`) from `main`, then
   `tools/package.sh <version> <dll> <exe>`.
3. **GitHub release:** the user publishes it, or it is published with the user's release script on their PC
   (location and use: the project's private notes, not this repository).
4. **Featured Content (do not skip):** the main menu's news panels come from the server, not from the mod
   (`docs/NEWS.md`). For every release, update the release panel:
   - in the repository: the `release-x.y.z` item of `server/HexRevival.Server/news.json` (id, url to the release page,
     title and text in the 11 languages);
   - on the VPS: the live file `/data/news.json` in the `hexrevival-server` container (the repository file is only
     copied there when the volume has none, so editing the repository is not enough):
     `docker cp news.json hexrevival-server:/data/news.json`, then check
     `curl -s http://127.0.0.1:7780/news.json` on the VPS. No restart needed.
   Keep one announcement only (the latest release, `"when": "noupdate"`): the user does not want older versions'
   panels shown.
5. **Server code:** if `server/` changed, update the VPS: `git pull` in its clone of the repository, then
   `docker compose up -d --build` in `server/` (the online protocol version decides whether older clients are locked out).

## Other rules
- **No private information about the server or the user's machines in this repository** (user, 2026-10-29): the
  public address and ports the game connects to are fine; logins, SSH access, server paths, account names, local
  paths, keys and passwords are not. They live in the project's private notes.
- GitHub accounts and what the cloud session can do: see the project doc `claude/github-setup.md`.
- Every value without a source is tagged `PLACEHOLDER` and listed in `docs/PLACEHOLDERS.md`.
- Game files never go into the repository.
