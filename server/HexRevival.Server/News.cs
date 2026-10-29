using System.Net;
using System.Text.RegularExpressions;

namespace HexRevival.Server;

/// <summary>
/// The "Featured Content" of the game's main menu, over plain HTTP (the game's Unity 5.6 has no modern TLS of its own):
///   GET /news.json      the panels ($HEX_DATA/news.json; the image's default copy when the file is missing)
///   GET /news/&lt;file&gt;   a picture of a panel ($HEX_DATA/news/, png or jpg)
/// Editing news.json in the data volume is enough: no rebuild, no restart (the file is read on each request).
/// The format is described in docs/NEWS.md.
/// </summary>
public static class News
{
    private static readonly Regex SafeName = new("^[A-Za-z0-9_.-]{1,80}\\.(png|jpg|jpeg)$", RegexOptions.IgnoreCase);
    private const long MaxFile = 4 * 1024 * 1024;

    public static async Task RunAsync(int port, string dataDir, CancellationToken stop)
    {
        string file = Path.Combine(dataDir, "news.json");
        string pictures = Path.Combine(dataDir, "news");
        try
        {
            Directory.CreateDirectory(pictures);
            string seed = Path.Combine(AppContext.BaseDirectory, "news.json");
            if (!File.Exists(file) && File.Exists(seed)) File.Copy(seed, file);
        }
        catch (Exception e) { Log.Info($"news: setup {e.Message}"); }

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://*:{port}/");
        try { listener.Start(); }
        catch (Exception e) { Log.Info($"news: cannot listen on port {port}: {e.Message}"); return; }
        Log.Info($"news on port {port} ({file})");
        using var reg = stop.Register(() => { try { listener.Stop(); } catch { } });
        while (!stop.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => Serve(ctx, file, pictures));
        }
    }

    private static async Task Serve(HttpListenerContext ctx, string file, string pictures)
    {
        HttpListenerResponse res = ctx.Response;
        try
        {
            string path = ctx.Request.Url?.AbsolutePath ?? "/";
            string? target = null, type = null;
            if (ctx.Request.HttpMethod != "GET" && ctx.Request.HttpMethod != "HEAD") res.StatusCode = 405;
            else if (path == "/news.json") { target = file; type = "application/json; charset=utf-8"; }
            else if (path.StartsWith("/news/") && SafeName.IsMatch(path.Substring(6)))
            {
                target = Path.Combine(pictures, path.Substring(6));
                type = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            }
            if (target != null && File.Exists(target) && new FileInfo(target).Length <= MaxFile)
            {
                byte[] body = await File.ReadAllBytesAsync(target);
                res.StatusCode = 200;
                res.ContentType = type;
                res.AddHeader("Cache-Control", "max-age=300");
                res.ContentLength64 = body.Length;
                if (ctx.Request.HttpMethod == "GET") await res.OutputStream.WriteAsync(body);
            }
            else if (res.StatusCode != 405) res.StatusCode = 404;
        }
        catch (Exception e) { Log.Info($"news: {e.Message}"); try { res.StatusCode = 500; } catch { } }
        finally { try { res.Close(); } catch { } }
    }
}
