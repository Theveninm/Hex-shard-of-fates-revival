namespace HexRevival.Server;

/// <summary>Classic token bucket: up to Burst units at once, refilled at Rate units per second.</summary>
public sealed class TokenBucket(double rate, double burst)
{
    private readonly double _burst = burst;
    private double _tokens = burst;
    private long _last = Environment.TickCount64;

    public bool Take(double amount = 1)
    {
        long now = Environment.TickCount64;
        _tokens = Math.Min(_burst, _tokens + (now - _last) / 1000.0 * rate);
        _last = now;
        if (_tokens < amount) return false;
        _tokens -= amount;
        return true;
    }
}
