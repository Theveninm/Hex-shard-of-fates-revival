# Notes for agents working on HEX Revival

## Releasing a new version (every step, in this order)

1. **Version number:** bump `Community.Version` (`mods/HexRevival/src/HexRevival/Community.cs`) and the launcher's
   `Version` and `AssemblyVersion` / `AssemblyFileVersion` (`tools/launcher/HexRevivalLauncher.cs`), in a pull request
   merged into `main` before building.
2. **Archive:** build `HexRevival.dll` (`build.sh`) and `HexRevival.exe` (`tools/launcher/build.sh`) from `main`, then
   `tools/package.sh <version> <dll> <exe>`.
3. **GitHub release:** the user publishes it, or it is published with the user's release script on their PC
   (location and use: the project's private notes, not this repository).
4. **Featured Content:** nothing to do. The mod builds its main menu panel itself from the latest GitHub release
   (its notes are the panel's text) and hides the frame when the player is up to date (`Community.cs`).
5. **Server code:** if `server/` changed, update the VPS: `git pull` in its clone of the repository, then
   `docker compose up -d --build` in `server/` (the online protocol version decides whether older clients are locked out).

## Other rules
- **No private information about the server or the user's machines in this repository** (user, 2026-10-29): the
  public address and ports the game connects to are fine; logins, SSH access, server paths, account names, local
  paths, keys and passwords are not. They live in the project's private notes.
- GitHub accounts and what the cloud session can do: see the project doc `claude/github-setup.md`.
- Every value without a source is tagged `PLACEHOLDER` and listed in `docs/PLACEHOLDERS.md`.
- Game files never go into the repository.
