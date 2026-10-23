# Multiplayer (prototype)

Status: **prototype working**. Tested on one PC with two game instances and a local server (2026-10-23): login and
registration, lobby, chat, challenge, then a full constructed game played to the end (both sides driven by the game AI,
dev command `aiplay`), back to the lobby afterwards.

Known limits of the prototype:
- constructed format only, no gems/equipment check, no online rewards (solo rewards are skipped online);
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
  accounts; it sees every deck (decks outside the campaign are shared) and keeps its favorites for online play.
  Offline accounts have no online feature at all.
- The **lobby** is the online account's main screen: your deck (Favorites / All decks, "+" / "-" to mark one), the
  challenges received and the open games (Join), **Host a game**, the connected players (Challenge), the chat, and the
  buttons Deck editor, Game settings, Online profile, Switch account. It opens again after each game.
- **Host a game** replaces the former Quick Match: your deck, then the opponent: "Player" opens the game in the lobby,
  "AI" plays one of your decks (those made for the AI first; the encounters and the game's decks are not offered).
- No cheat menu on the online account, and its option is hidden from the game's settings.
- Code: `NetClient.cs` (connection), `OnlineLobby.cs` (lobby and tab), `OnlineMatch.cs` (the game itself).

How the game itself works:
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
