# Multiplayer (prototype)

Status: **work in progress**. The master server works and is tested (`server/smoke_test.py`); the game client side is being written.

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

Lobby events: `welcome {id, name, players, formats}`, `joined {player}`, `left {id}`, `status {id, status}`
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
