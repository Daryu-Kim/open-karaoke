using OpenKaraoke.Core.Library;

namespace OpenKaraoke.App.Tests;

public class SongLibraryStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "oktest_" + Guid.NewGuid().ToString("N"));

    private SqliteSongLibraryStore CreateStore()
    {
        return new SqliteSongLibraryStore(Path.Combine(_tempDir, "songs.db"));
    }

    private static SongRecord Song(
        string videoId,
        string title = "밤편지",
        string artist = "아이유",
        string? tjNumber = "12345",
        DateTime? addedAt = null)
    {
        return new SongRecord
        {
            VideoId = videoId,
            Title = title,
            Artist = artist,
            TjNumber = tjNumber,
            LocalPath = Path.Combine("C:\\data\\songs", videoId + ".m4a"),
            DurationSeconds = 253,
            FileSizeBytes = 3_200_000,
            AddedAtUtc = addedAt ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
    }

    [Fact]
    public async Task Initialize_CreatesSchema_AndStartsEmpty()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        Assert.Equal(0, await store.CountAsync());
        Assert.True(File.Exists(Path.Combine(_tempDir, "songs.db")));
    }

    [Fact]
    public async Task Add_ThenGet_ById_And_ByVideoId()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        SongRecord saved = await store.AddOrUpdateAsync(Song("abc123"));

        Assert.True(saved.Id > 0);
        Assert.Equal("abc123", (await store.GetByVideoIdAsync("abc123"))!.VideoId);
        SongRecord? byId = await store.GetByIdAsync(saved.Id);
        Assert.NotNull(byId);
        Assert.Equal("밤편지", byId.Title);
        Assert.Equal("12345", byId.TjNumber);
        Assert.Equal(253, byId.DurationSeconds);
    }

    [Fact]
    public async Task Add_DuplicateVideoId_UpdatesMetadata_KeepsIdAndAddedDate()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        DateTime added = new(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        SongRecord first = await store.AddOrUpdateAsync(Song("dup1", addedAt: added));
        SongRecord second = await store.AddOrUpdateAsync(Song(
            "dup1",
            title: "새 제목",
            artist: "새 가수",
            tjNumber: "99999",
            addedAt: DateTime.UtcNow.AddDays(1)));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await store.CountAsync());

        SongRecord? stored = await store.GetByVideoIdAsync("dup1");
        Assert.NotNull(stored);
        Assert.Equal("새 제목", stored.Title);
        Assert.Equal("새 가수", stored.Artist);
        Assert.Equal("99999", stored.TjNumber);
        Assert.Equal(added, stored.AddedAtUtc);
    }

    [Fact]
    public async Task Search_MatchesTitle_Artist_AndTjNumber()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        await store.AddOrUpdateAsync(Song("a1", title: "좋은날", artist: "아이유", tjNumber: "111"));
        await store.AddOrUpdateAsync(Song("a2", title: "밤편지", artist: "아이유", tjNumber: "222"));
        await store.AddOrUpdateAsync(Song("a3", title: "벚꽃엔딩", artist: "버스커버스커", tjNumber: "333"));

        Assert.Equal(2, (await store.GetAllAsync("아이유")).Count);
        Assert.Single(await store.GetAllAsync("밤편지"));
        Assert.Single(await store.GetAllAsync("222"));
        Assert.Equal(3, (await store.GetAllAsync(null)).Count);
        Assert.Equal(3, (await store.GetAllAsync("  ")).Count);
    }

    [Fact]
    public async Task Search_EscapesLikeWildcards()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        await store.AddOrUpdateAsync(Song("w1", title: "100% 사랑", artist: "가수A"));
        await store.AddOrUpdateAsync(Song("w2", title: "100X 사랑", artist: "가수A"));

        // Literal '%' must not act as a wildcard.
        Assert.Single(await store.GetAllAsync("100%"));
    }

    [Fact]
    public async Task GetAll_OrdersByNewestFirst()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        await store.AddOrUpdateAsync(Song("o1", title: "오래된곡", addedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await store.AddOrUpdateAsync(Song("o2", title: "최신곡", addedAt: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)));

        List<SongRecord> all = await store.GetAllAsync();
        Assert.Equal("최신곡", all[0].Title);
        Assert.Equal("오래된곡", all[1].Title);
    }

    [Fact]
    public async Task Delete_RemovesRow_AndReturnsFalseForMissing()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        SongRecord saved = await store.AddOrUpdateAsync(Song("del1"));

        Assert.True(await store.DeleteAsync(saved.Id));
        Assert.Null(await store.GetByIdAsync(saved.Id));
        Assert.False(await store.DeleteAsync(saved.Id));
        Assert.Equal(0, await store.CountAsync());
    }

    [Fact]
    public async Task ReopenDatabase_PersistsRows()
    {
        string path = Path.Combine(_tempDir, "songs.db");
        var first = new SqliteSongLibraryStore(path);
        await first.InitializeAsync();
        await first.AddOrUpdateAsync(Song("persist1"));

        var second = new SqliteSongLibraryStore(path);
        await second.InitializeAsync();
        Assert.Equal(1, await second.CountAsync());
        Assert.NotNull(await second.GetByVideoIdAsync("persist1"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }
}
