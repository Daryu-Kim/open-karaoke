using OpenKaraoke.Core.RateLimit;

namespace OpenKaraoke.App.Tests;

public class RateLimiterTests
{
    private static readonly DateTime Now = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TryAcquire_AllowsUpToLimitWithinWindow()
    {
        var limiter = new RateLimiter(3);

        Assert.True(limiter.TryAcquire(Now));
        Assert.True(limiter.TryAcquire(Now.AddSeconds(1)));
        Assert.True(limiter.TryAcquire(Now.AddSeconds(2)));
        Assert.False(limiter.TryAcquire(Now.AddSeconds(3)));
    }

    [Fact]
    public void TryAcquire_ExpiredWindowFreesSlot()
    {
        var limiter = new RateLimiter(1);

        Assert.True(limiter.TryAcquire(Now));
        Assert.False(limiter.TryAcquire(Now.AddSeconds(30)));

        // 61 seconds later the first stamp falls outside the window.
        Assert.True(limiter.TryAcquire(Now.AddSeconds(61)));
    }

    [Fact]
    public void Reset_ClearsHistory()
    {
        var limiter = new RateLimiter(1);
        Assert.True(limiter.TryAcquire(Now));
        Assert.False(limiter.TryAcquire(Now.AddSeconds(1)));

        limiter.Reset();
        Assert.True(limiter.TryAcquire(Now.AddSeconds(1)));
    }

    [Fact]
    public void Constructor_ClampsLimitToAtLeastOne()
    {
        var limiter = new RateLimiter(0);
        Assert.Equal(1, limiter.MaxPerMinute);
    }
}
