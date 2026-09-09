using OpenKaraoke.Core.Caching;
using OpenKaraoke.Core.Search;

namespace OpenKaraoke.App.Tests;

public class SearchCacheTests
{
    private static SearchVideoItem Item(string id) => new()
    {
        VideoId = id,
        Title = id,
        ChannelTitle = "채널",
    };

    private static readonly DateTime Now = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Get_MissingKey_ReturnsNull()
    {
        var cache = new SearchCache(TimeSpan.FromMinutes(10), 100);
        Assert.Null(cache.Get("없는곡", Now));
    }

    [Fact]
    public void SetThenGet_ReturnsStoredItems()
    {
        var cache = new SearchCache(TimeSpan.FromMinutes(10), 100);
        var items = new[] { Item("a"), Item("b") };
        cache.Set("밤편지", items, Now);

        IReadOnlyList<SearchVideoItem>? result = cache.Get("밤편지", Now);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal("a", result[0].VideoId);
    }

    [Fact]
    public void Get_AfterTtl_ReturnsNull()
    {
        var cache = new SearchCache(TimeSpan.FromMinutes(10), 100);
        cache.Set("곡", new[] { Item("a") }, Now);

        Assert.Null(cache.Get("곡", Now.AddMinutes(10).AddSeconds(1)));
    }

    [Fact]
    public void Set_BeyondMaxEntries_EvictsLeastRecentlyUsed()
    {
        var cache = new SearchCache(TimeSpan.FromMinutes(10), 3);
        cache.Set("q1", new[] { Item("1") }, Now);
        cache.Set("q2", new[] { Item("2") }, Now);
        cache.Set("q3", new[] { Item("3") }, Now);

        // Touch q1 to make q2 the least recently used.
        cache.Get("q1", Now.AddSeconds(1));
        cache.Set("q4", new[] { Item("4") }, Now.AddSeconds(2));

        Assert.NotNull(cache.Get("q1", Now.AddSeconds(3)));
        Assert.Null(cache.Get("q2", Now.AddSeconds(3)));
        Assert.NotNull(cache.Get("q3", Now.AddSeconds(3)));
        Assert.NotNull(cache.Get("q4", Now.AddSeconds(3)));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var cache = new SearchCache(TimeSpan.FromMinutes(10), 100);
        cache.Set("곡", new[] { Item("a") }, Now);
        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Null(cache.Get("곡", Now));
    }
}
