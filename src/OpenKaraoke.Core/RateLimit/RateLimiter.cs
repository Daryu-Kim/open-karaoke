namespace OpenKaraoke.Core.RateLimit;

/// <summary>
/// Sliding-window rate limiter (requests per minute). Protects the shared YouTube
/// API quota from accidental search abuse.
/// </summary>
public sealed class RateLimiter
{
    private readonly object _gate = new();
    private readonly int _maxPerMinute;
    private readonly Queue<DateTime> _timestamps = new();

    public RateLimiter(int maxPerMinute)
    {
        _maxPerMinute = Math.Max(1, maxPerMinute);
    }

    public int MaxPerMinute => _maxPerMinute;

    public bool TryAcquire(DateTime utcNow)
    {
        lock (_gate)
        {
            while (_timestamps.Count > 0 && _timestamps.Peek() <= utcNow.AddMinutes(-1))
            {
                _timestamps.Dequeue();
            }

            if (_timestamps.Count >= _maxPerMinute)
            {
                return false;
            }

            _timestamps.Enqueue(utcNow);
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _timestamps.Clear();
        }
    }
}
