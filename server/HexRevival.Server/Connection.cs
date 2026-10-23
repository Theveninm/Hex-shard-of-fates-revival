using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace HexRevival.Server;

/// <summary>
/// One game client: reads frames, runs the login handshake, then hands messages to the lobby.
/// Sending never blocks the caller: frames go to a bounded queue drained by a writer task, and a client that
/// cannot keep up is dropped.
/// </summary>
public sealed class Connection
{
    public const int ProtocolVersion = 1;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(20);

    private readonly TcpClient _tcp;
    private readonly Lobby _lobby;
    private readonly Accounts _accounts;
    private readonly Channel<byte[]> _outbox = Channel.CreateBounded<byte[]>(512);
    private readonly CancellationTokenSource _cts = new();
    private readonly byte[] _nonce = RandomNumberGenerator.GetBytes(32);

    // Flood protection: control messages and relayed bytes have separate budgets.
    private readonly TokenBucket _messages = new(rate: 20, burst: 60);
    private readonly TokenBucket _relayBytes = new(rate: 256 * 1024, burst: 2 * 1024 * 1024);

    public IPAddress Ip { get; }
    public Account? Account { get; private set; }

    // Lobby state, only touched under the lobby lock.
    public Match? Match { get; set; }
    public Connection? ChallengeTo { get; set; }
    public DateTime ChallengeAt { get; set; }
    public string ChallengeFormat { get; set; } = "";
    public TokenBucket Chat { get; } = new(rate: 1, burst: 5);

    public Connection(TcpClient tcp, Lobby lobby, Accounts accounts)
    {
        _tcp = tcp;
        _lobby = lobby;
        _accounts = accounts;
        Ip = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address;
    }

    public string Label => Account != null ? $"{Account.Name} ({Ip})" : Ip.ToString();

    public async Task RunAsync()
    {
        _tcp.NoDelay = true;
        var stream = _tcp.GetStream();
        var writer = WriteLoopAsync(stream);
        try
        {
            Send(new JsonObject { ["t"] = "hello", ["v"] = ProtocolVersion, ["nonce"] = Convert.ToBase64String(_nonce), ["iterations"] = Accounts.Iterations });
            while (!_cts.IsCancellationRequested)
            {
                // A logged-in client pings at least every 30 s; the login itself must come quickly.
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(Account == null ? LoginTimeout : IdleTimeout);
                var frame = await Frame.ReadAsync(stream, timeout.Token);
                if (frame == null) break;
                if (!Handle(frame.Value.Kind, frame.Value.Payload)) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or InvalidDataException or SocketException or System.Text.Json.JsonException or FormatException)
        {
            Log.Info($"{Label}: {e.Message}");
        }
        finally
        {
            if (Account != null) _lobby.Leave(this);
            // Let the writer send what is queued (an error message), then stop it.
            _outbox.Writer.TryComplete();
            await Task.WhenAny(writer, Task.Delay(2000));
            await _cts.CancelAsync();
            try { await writer; } catch { }
            _tcp.Dispose();
        }
    }

    /// <summary>False closes the connection.</summary>
    private bool Handle(byte kind, byte[] payload)
    {
        if (kind == Frame.Relay)
        {
            if (Account == null || !_relayBytes.Take(payload.Length)) return Fail("rate", "Too much game data.");
            _lobby.Relay(this, payload);
            return true;
        }
        if (!_messages.Take()) return Fail("rate", "Too many messages.");

        var msg = JsonNode.Parse(payload) as JsonObject ?? throw new InvalidDataException("not a JSON object");
        string type = (string?)msg["t"] ?? "";
        if (type == "ping") { Send(new JsonObject { ["t"] = "pong" }); return true; }
        if (Account == null) return Login(type, msg);
        return _lobby.Handle(this, type, msg);
    }

    private bool Login(string type, JsonObject msg)
    {
        if ((int?)msg["v"] != ProtocolVersion) return Fail("version", "Please update HEX Revival.");
        if (!_lobby.LoginAllowed(Ip)) return Fail("throttled", "Too many failed logins, try again in a minute.");

        string? id = (string?)msg["id"];
        string? name = ((string?)msg["name"])?.Trim();
        if (!Accounts.ValidId(id)) return Fail("bad_id", "Invalid player id.");
        if (name != null && !Accounts.ValidName(name)) return Fail("bad_name", "Names are 3 to 20 letters, digits, '_' or '-'.");

        Account? account;
        if (type == "register")
        {
            byte[] key = Convert.FromBase64String((string?)msg["key"] ?? "");
            if (key.Length != 32 || name == null) return Fail("bad_request", "Invalid registration.");
            string? error = _accounts.Register(id!, name, key);
            if (error != null) return Fail(error, error == "name_taken" ? "This name is already used." : "This player id already exists.");
            account = _accounts.Find(id!)!;
            Log.Info($"new account {name} ({Ip})");
        }
        else if (type == "login")
        {
            account = _accounts.Find(id!);
            byte[] proof = Convert.FromBase64String((string?)msg["proof"] ?? "");
            if (account == null) return Fail("unknown_id", "No account with this player id.");
            if (!Accounts.CheckProof(account, _nonce, proof))
            {
                _lobby.LoginFailed(Ip);
                return Fail("bad_password", "Wrong password.");
            }
            if (!_accounts.Touch(account, name)) Send(new JsonObject { ["t"] = "notice", ["code"] = "name_taken", ["msg"] = "This name is already used, the old one is kept." });
        }
        else return Fail("bad_request", "Log in first.");

        if (!_lobby.Join(this, account)) return Fail("already_online", "This account is already connected.");
        Account = account;
        return true;
    }

    /// <summary>Sends an error and closes.</summary>
    private bool Fail(string code, string text)
    {
        Send(new JsonObject { ["t"] = "error", ["code"] = code, ["msg"] = text });
        return false;
    }

    public void Send(JsonObject msg) => Enqueue(Frame.Build(Frame.Json, Encoding.UTF8.GetBytes(msg.ToJsonString())));

    public void SendRelay(byte[] payload) => Enqueue(Frame.Build(Frame.Relay, payload));

    private void Enqueue(byte[] frame)
    {
        if (!_outbox.Writer.TryWrite(frame))
        {
            Log.Info($"{Label}: send queue full, dropped");
            _cts.Cancel();
        }
    }

    public void Close() => _cts.Cancel();

    private async Task WriteLoopAsync(Stream stream)
    {
        try
        {
            await foreach (var frame in _outbox.Reader.ReadAllAsync(_cts.Token))
                await stream.WriteAsync(frame, _cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            // Flush what is left (an error message before closing) without waiting long.
            while (_outbox.Reader.TryRead(out var frame))
                try { stream.Write(frame); } catch { break; }
            _cts.Cancel();
        }
    }
}
