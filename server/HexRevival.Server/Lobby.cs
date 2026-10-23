using System.Net;
using System.Text.Json.Nodes;

namespace HexRevival.Server;

/// <summary>
/// A game between two players. The host (seat 0, the challenger) runs the authoritative game session on its PC;
/// the guest (seat 1) only displays it. The server relays their game data without reading it.
/// </summary>
public sealed class Match(int id, string format, Connection host, Connection guest)
{
    public int Id { get; } = id;
    public string Format { get; } = format;
    public Connection Host { get; } = host;
    public Connection Guest { get; } = guest;
    public Connection Opponent(Connection c) => c == Host ? Guest : Host;
}

/// <summary>
/// Everything players share once logged in: who is online, the chat, challenges and matches.
/// One lock guards all of it: messages are tiny and handled quickly, and sending only queues bytes.
/// </summary>
public sealed class Lobby
{
    public static readonly string[] Formats = ["constructed"];
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromSeconds(60);
    private const int MaxChat = 300;

    private readonly object _lock = new();
    private readonly Dictionary<string, Connection> _online = new(StringComparer.Ordinal);
    private readonly Dictionary<IPAddress, (int Count, DateTime Since)> _failedLogins = new();
    private int _nextMatch = 1;

    public int OnlineCount { get { lock (_lock) return _online.Count; } }

    // ------------------------------------------------------------------
    // Login and logout
    // ------------------------------------------------------------------

    /// <summary>5 wrong passwords per address and minute, then wait.</summary>
    public bool LoginAllowed(IPAddress ip)
    {
        lock (_lock)
            return !_failedLogins.TryGetValue(ip, out var f) || DateTime.UtcNow - f.Since > TimeSpan.FromMinutes(1) || f.Count < 5;
    }

    public void LoginFailed(IPAddress ip)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            _failedLogins[ip] = _failedLogins.TryGetValue(ip, out var f) && now - f.Since <= TimeSpan.FromMinutes(1) ? (f.Count + 1, f.Since) : (1, now);
        }
    }

    /// <summary>False when the account is already online.</summary>
    public bool Join(Connection c, Account a)
    {
        lock (_lock)
        {
            if (_online.ContainsKey(a.Id)) return false;
            var players = new JsonArray();
            foreach (var o in _online.Values) players.Add(PlayerInfo(o));
            c.Send(new JsonObject { ["t"] = "welcome", ["id"] = a.Id, ["name"] = a.Name, ["players"] = players, ["formats"] = new JsonArray(Formats.Select(f => (JsonNode)f).ToArray()) });
            _online[a.Id] = c;
            Broadcast(new JsonObject { ["t"] = "joined", ["player"] = PlayerInfo(c, a) }, except: c);
            Log.Info($"{a.Name} joined ({_online.Count} online)");
            return true;
        }
    }

    public void Leave(Connection c)
    {
        lock (_lock)
        {
            if (c.Account == null || !_online.Remove(c.Account.Id)) return;
            EndMatch(c, "disconnected");
            CancelChallenge(c);
            foreach (var o in _online.Values)
                if (o.ChallengeTo == c) { o.ChallengeTo = null; o.Send(new JsonObject { ["t"] = "challenge_end", ["with"] = c.Account.Id, ["reason"] = "offline" }); }
            Broadcast(new JsonObject { ["t"] = "left", ["id"] = c.Account.Id });
            Log.Info($"{c.Account.Name} left ({_online.Count} online)");
        }
    }

    // ------------------------------------------------------------------
    // Messages
    // ------------------------------------------------------------------

    /// <summary>A control message from a logged-in player. False closes the connection.</summary>
    public bool Handle(Connection c, string type, JsonObject msg)
    {
        lock (_lock)
        {
            switch (type)
            {
                case "chat": return OnChat(c, ((string?)msg["text"])?.Trim());
                case "challenge": OnChallenge(c, (string?)msg["to"], (string?)msg["format"] ?? Formats[0]); return true;
                case "cancel": CancelChallenge(c); return true;
                case "accept": OnAnswer(c, (string?)msg["from"], accept: true); return true;
                case "decline": OnAnswer(c, (string?)msg["from"], accept: false); return true;
                case "leave": EndMatch(c, "left"); return true;
                default:
                    c.Send(Error("bad_request", $"Unknown message '{type}'."));
                    return true;
            }
        }
    }

    private bool OnChat(Connection c, string? text)
    {
        if (string.IsNullOrEmpty(text)) return true;
        if (text.Length > MaxChat) text = text[..MaxChat];
        if (!c.Chat.Take()) { c.Send(Error("rate", "You are sending messages too fast.")); return true; }
        Broadcast(new JsonObject { ["t"] = "chat", ["from"] = c.Account!.Id, ["name"] = c.Account.Name, ["text"] = text, ["at"] = DateTime.UtcNow.ToString("o") });
        return true;
    }

    private void OnChallenge(Connection c, string? to, string format)
    {
        if (!Formats.Contains(format)) { c.Send(Error("bad_format", "Unknown game format.")); return; }
        if (to == null || !_online.TryGetValue(to, out var target) || target == c) { c.Send(Error("not_online", "This player is not online.")); return; }
        if (c.Match != null || target.Match != null) { c.Send(Error("busy", "A player is already in a game.")); return; }
        CancelChallenge(c);   // one challenge at a time
        c.ChallengeTo = target;
        c.ChallengeAt = DateTime.UtcNow;
        c.ChallengeFormat = format;
        target.Send(new JsonObject { ["t"] = "challenged", ["from"] = c.Account!.Id, ["name"] = c.Account.Name, ["format"] = format });
        c.Send(new JsonObject { ["t"] = "challenge_sent", ["to"] = to, ["format"] = format });
    }

    private void CancelChallenge(Connection c)
    {
        var target = c.ChallengeTo;
        if (target == null) return;
        c.ChallengeTo = null;
        target.Send(new JsonObject { ["t"] = "challenge_end", ["with"] = c.Account!.Id, ["reason"] = "cancelled" });
    }

    /// <summary>The challenged player's answer; c is the one answering.</summary>
    private void OnAnswer(Connection c, string? from, bool accept)
    {
        if (from == null || !_online.TryGetValue(from, out var challenger) || challenger.ChallengeTo != c
            || DateTime.UtcNow - challenger.ChallengeAt > ChallengeLifetime)
        {
            c.Send(Error("no_challenge", "This challenge is no longer valid."));
            return;
        }
        challenger.ChallengeTo = null;
        if (!accept)
        {
            challenger.Send(new JsonObject { ["t"] = "challenge_end", ["with"] = c.Account!.Id, ["reason"] = "declined" });
            return;
        }
        if (c.Match != null || challenger.Match != null) { c.Send(Error("busy", "A player is already in a game.")); return; }

        // Both players stop challenging anyone else.
        CancelChallenge(c);
        var match = new Match(_nextMatch++, challenger.ChallengeFormat, host: challenger, guest: c);
        challenger.Match = c.Match = match;
        foreach (var p in new[] { match.Host, match.Guest })
        {
            var opponent = match.Opponent(p);
            p.Send(new JsonObject
            {
                ["t"] = "match", ["match"] = match.Id, ["format"] = match.Format,
                // Seat 0 hosts the game. The seats also give both players distinct ids inside the game.
                ["seat"] = p == match.Host ? 0 : 1,
                ["opponent"] = new JsonObject { ["id"] = opponent.Account!.Id, ["name"] = opponent.Account.Name },
            });
        }
        BroadcastStatus(match.Host);
        BroadcastStatus(match.Guest);
        Log.Info($"match {match.Id}: {match.Host.Account!.Name} hosts {match.Guest.Account!.Name}");
    }

    private void EndMatch(Connection c, string reason)
    {
        var match = c.Match;
        if (match == null) return;
        match.Host.Match = match.Guest.Match = null;
        match.Opponent(c).Send(new JsonObject { ["t"] = "match_end", ["match"] = match.Id, ["reason"] = reason });
        c.Send(new JsonObject { ["t"] = "match_end", ["match"] = match.Id, ["reason"] = reason });
        BroadcastStatus(match.Host);
        BroadcastStatus(match.Guest);
        Log.Info($"match {match.Id} ended ({reason})");
    }

    /// <summary>Game data goes to the opponent unchanged; outside a match it is ignored.</summary>
    public void Relay(Connection c, byte[] payload)
    {
        lock (_lock)
            c.Match?.Opponent(c).SendRelay(payload);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static JsonObject PlayerInfo(Connection c, Account? a = null)
    {
        a ??= c.Account!;
        return new JsonObject { ["id"] = a.Id, ["name"] = a.Name, ["status"] = c.Match != null ? "playing" : "idle" };
    }

    private void BroadcastStatus(Connection c)
    {
        if (c.Account != null && _online.ContainsKey(c.Account.Id))
            Broadcast(new JsonObject { ["t"] = "status", ["id"] = c.Account.Id, ["status"] = c.Match != null ? "playing" : "idle" });
    }

    private void Broadcast(JsonObject msg, Connection? except = null)
    {
        foreach (var o in _online.Values)
            if (o != except) o.Send(msg);
    }

    private static JsonObject Error(string code, string text) => new() { ["t"] = "error", ["code"] = code, ["msg"] = text };
}
