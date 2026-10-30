using System.Net;
using System.Net.Sockets;
using HexRevival.Server;

// HEX Revival master server: accounts, lobby, chat, challenges and relay of game data between two players.
// Settings come from environment variables so the Docker container needs no config file.
int port = Env("HEX_PORT", 7777);
int maxClients = Env("HEX_MAX_CLIENTS", 200);
int maxPerIp = Env("HEX_MAX_PER_IP", 4);
string dataDir = Environment.GetEnvironmentVariable("HEX_DATA") ?? Path.Combine(AppContext.BaseDirectory, "data");

var accounts = new Accounts(dataDir);
var lobby = new Lobby();
var perIp = new Dictionary<IPAddress, int>();
int clients = 0;

// IPv4 and IPv6 on one socket when the host has IPv6 (Docker often has not).
var listener = new TcpListener(Socket.OSSupportsIPv6 ? IPAddress.IPv6Any : IPAddress.Any, port);
if (Socket.OSSupportsIPv6) listener.Server.DualMode = true;
listener.Start();
Log.Info($"HEX Revival server on port {port}, {accounts.Count} accounts, data in {dataDir}");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();

try
{
    while (!stop.IsCancellationRequested)
    {
        TcpClient tcp = await listener.AcceptTcpClientAsync(stop.Token);
        var ip = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        lock (perIp)
        {
            if (clients >= maxClients || perIp.GetValueOrDefault(ip) >= maxPerIp)
            {
                Log.Info($"refused {ip}: too many connections");
                tcp.Dispose();
                continue;
            }
            clients++;
            perIp[ip] = perIp.GetValueOrDefault(ip) + 1;
        }
        _ = Task.Run(async () =>
        {
            try { await new Connection(tcp, lobby, accounts).RunAsync(); }
            catch (Exception e) { Log.Info($"{ip}: unexpected {e}"); }
            finally
            {
                lock (perIp)
                {
                    clients--;
                    if (--perIp[ip] == 0) perIp.Remove(ip);
                }
            }
        });
    }
}
catch (OperationCanceledException) { }
Log.Info("stopped");

static int Env(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out int v) ? v : fallback;
