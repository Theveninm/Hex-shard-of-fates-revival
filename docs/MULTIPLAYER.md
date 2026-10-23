# Multiplayer (prototype)

Status: **prototype working**. Tested on one PC with two game instances and a local server (2026-10-23): login and
registration, lobby, chat, challenge, then a full constructed game played to the end (both sides driven by the game AI,
dev command `aiplay`), back to the lobby afterwards.

Known limits of the prototype:
- no gems/equipment check; no online rewards (solo rewards are skipped online);
- if a player leaves during a game, the other one gets a notice and must leave the battle themselves;
- no reconnection, no chess clock (the solo "never expires" rule applies);
- the host runs the rules, so a modified host could cheat (hidden information stays hidden: the guest only
  receives its own events).

## Architecture

```
 player A (host)                 master server (VPS, Docker)                player B (guest)
 runs the game session  <──TCP──>  accounts, lobby, chat,     <──TCP──>   displays the game,
 (StandaloneSession)               challenges, relay                       sends its actions
```

- One small C# server (`server/HexRevival.Server`, .NET 8, no dependency) holds the accounts and the lobby.
- A game is played between two players: the challenger **hosts** it (seat 0) and runs the rules on its PC exactly
  like a solo game, the opponent (seat 1) only displays it. The server relays their game data without reading it,
  so no port has to be opened on the players' side.
- Game data is the game's own network format (`EncData`), see `NetCodec.cs` in the mod.

## In the game

- Login screen, **Online** row: logs in with the online profile and opens the lobby (the first time, the profile form:
  name, player id, password with "Generate", server address; saved in `HexRevivalSaves/online_profile.json`).
- The online profile has its own account (`HexRevivalOnline/`, everything unlocked), separate from the offline
  accounts. Offline accounts have no online feature at all.
- Decks (user's choice, 2026-10-23): **each account has its own decks** (campaign and player decks, in its save);
  nothing is shared between offline accounts any more, except the decks made for the AI (`decks_common.txt`), which
  are AI opponents. The online account edits only its own decks and plays every offline account's decks, campaign
  ones included (with their PvE champion), listed by account; they cannot be edited from there.
- A deck saved as **made for the AI** stays one (it is shared and has no owner any more): its box can no longer be
  unticked; it can still be edited or deleted, and **Copy as a player deck** (deck editor, AI settings window) copies
  it into the current account if the account owns every card (accounts with everything unlocked and the online
  account do; otherwise the missing cards are listed).
- **Formats** (the server knows the same list): `immortal`, `standard`, `pve` (PvE cards and campaign champions)
  checked with the game's own deck validator (every card owned online), and `free` (any deck). A game in the lobby
  shows its format; the deck list has a format filter; clicking a game or a challenge selects its format, and
  Join / Accept need a deck valid for it. "Host a game" picks the format and lists only the decks valid for it.
  The host checks the guest's deck again against the format and cancels the game when it is not valid.
- The **lobby** is the online account's main screen: a banner with the Discord server (and, when there is one, the
  newer version to download: players with different versions may not be able to play together), your deck (Favorites / All decks by account, "+" / "-" to mark a
  favorite), the
  challenges received and the open games (Join), **Host a game**, the connected players (Challenge), the chat, and the
  buttons Deck editor, Game settings, Online profile, Switch account (also Esc). It opens again after each game.
- **Host a game** replaces the former Quick Match: your deck, then the opponent: "Player" opens the game in the lobby
  (in the chosen format), "AI" plays a deck made for the AI or a player deck, listed by account (the encounters and
  the game's decks are not offered).
- No cheat menu on the online account, and its option is hidden from the game's settings.
- Code: `NetClient.cs` (connection), `OnlineLobby.cs` (lobby and tab), `OnlineMatch.cs` (the game itself).

How the game itself works:
- the guest sends its deck (cards, champion, and for a campaign deck its PvE champion: race, class, level, talents);
  the host plays it under a fixed id (`Decks.SetRemote`) given to the opponent seat; the guest enters the battle once
  the host's session state arrives;
- host: a normal solo encounter whose opponent seat (`ServicePlayer.4098`) is not an AI; its events go to the network
  (`Server.Flush`), the guest's transactions are submitted to the host's session, and a transaction without visible
  effect gets an empty packet as answer (the game client waits for one);
- guest: no session at all; `SendStartEncounter` waits for the host's session state, `SendJoinSession` joins like a
  network client, `SendSessionTransaction` sends to the host;
- both games call their own player `ServicePlayer.4097`: the guest swaps 4097 and 4098 in everything it receives and
  sends (raw ids in the events, field by field in transactions and the session state).

Testing on one PC: start a second game with `Hex.exe -hexinstance 2 -logFile "<path>\output_log_2.txt"`. It uses its
own profile (`online_profile_2.json`) and online account (`HexRevivalOnline_2/`), logs in online with the
autostart file, and has its own dev command files (`HexRevival.cmd.2`). The keyword `online` in `HexRevival.autostart`
makes the first instance log in online too.

## Wire format

TCP, frames `[int32 big-endian length][byte kind][payload]`:

| kind | payload | max |
|------|---------|-----|
| 1 | JSON control message, UTF-8, `{"t": "<type>", ...}` | 8 KB |
| 2 | game data, forwarded unchanged to the match opponent | 1 MB |

## Accounts and login

The game's Unity version cannot do modern TLS, so the password never travels:

1. The player id (32 hex chars) and the password are in `HexRevivalSaves/online_profile.json`; copying that file
   moves the account to another PC.
2. The client derives `key = PBKDF2-HMAC-SHA1(password, salt = player id, iterations, 32 bytes)`
   (`iterations` is announced in `hello`; SHA1 because .NET 3.5 only has that one).
3. `register` sends the key once; `login` sends `HMAC-SHA256(key, nonce)` where the nonce is random per connection.
4. The server stores the key, never the password.

Names are unique: compared without case, spaces, `_` and `-` ("Some_One" is "some one"), and the names listed in
`reserved_names.txt` (data folder, created with admin, moderator, HEX Revival...; read again when edited) cannot be
taken. A refused name at registration or at login (renaming) opens the profile form with the reason; the client always
shows the name the server confirms (`welcome`).

Limit: someone able to read the traffic of the **registration** could log in as that player. Fine for a community
prototype; a TLS tunnel (stunnel) or a key exchange can be added later without changing the accounts.

## Messages

Server → client on connection: `hello {v, nonce, iterations}`.

| client → server | effect |
|-----------------|--------|
| `register {v, id, name, key}` | creates the account and joins the lobby |
| `login {v, id, proof, name?}` | joins the lobby (a new free name renames the account) |
| `ping` | `pong` (send one at least every 30 s, the server closes after 90 s of silence) |
| `chat {text}` | `chat {from, name, text, at}` to everyone (300 chars, 1/s) |
| `challenge {to, format}` | `challenged {from, name, format}` to the target, `challenge_sent` back |
| `cancel` | withdraws the challenge: `challenge_end {with, reason}` |
| `accept {from}` / `decline {from}` | `match {match, format, seat, opponent}` to both / `challenge_end` |
| `leave` | ends the match: `match_end {match, reason}` to both |
| `create {format}` | opens a game (one per player): `game_added {game}` to everyone |
| `close` | closes our open game: `game_removed {game}` |
| `join {game}` | `match` to both, the creator hosts |

Lobby events: `welcome {id, name, players, games, formats}`, `joined {player}`, `left {id}`, `status {id, status}`
(`idle` / `playing`). Errors: `error {code, msg}`; after a login error the server closes the connection.

## Protections

Per address: 4 connections, 5 wrong passwords per minute. Per connection: 20 messages/s (burst 60),
256 KB/s of game data (burst 2 MB), a send queue of 512 frames (a client that does not read is dropped).
200 connections in total. All configurable with environment variables (`HEX_PORT`, `HEX_MAX_CLIENTS`,
`HEX_MAX_PER_IP`, `HEX_DATA`).

## Running the server

```
cd server
docker compose up -d --build        # port 7777, accounts in the volume hexrevival-data
python3 smoke_test.py 127.0.0.1     # end-to-end check (creates two test accounts)
```

Without Docker: `dotnet run -c Release --project server/HexRevival.Server`.
