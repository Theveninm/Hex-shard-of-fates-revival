namespace HexRevival.Server;

/// <summary>One line per event on stdout (docker logs shows it).</summary>
public static class Log
{
    private static readonly object s_Lock = new();

    public static void Info(string text)
    {
        lock (s_Lock) Console.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} {text}");
    }
}
