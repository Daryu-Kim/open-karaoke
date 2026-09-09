using OpenKaraoke.Core.Search;

namespace OpenKaraoke.App.Tests;

public class SearchResultScorerTests
{
    private static SearchVideoItem Item(string videoId, string title) => new()
    {
        VideoId = videoId,
        Title = title,
        ChannelTitle = "채널",
    };

    private readonly SearchResultScorer _scorer = new();

    [Fact]
    public void Score_MrMarkersBoost()
    {
        Assert.True(_scorer.Score("밤편지 노래방 MR") > _scorer.Score("밤편지"));
        Assert.True(_scorer.Score("밤편지 instrumental") > _scorer.Score("밤편지"));
        Assert.True(_scorer.Score("밤편지 반주") > _scorer.Score("밤편지"));
    }

    [Fact]
    public void Score_PenaltyMarkersLowerScore()
    {
        Assert.True(_scorer.Score("밤편지 가사") < _scorer.Score("밤편지"));
        Assert.True(_scorer.Score("밤편지 라이브") < _scorer.Score("밤편지"));
        Assert.True(_scorer.Score("밤편지 cover") < _scorer.Score("밤편지"));
    }

    [Fact]
    public void Score_MrWithPenalty_NetsAgainstPlain()
    {
        // "밤편지 MR 라이브": +25 -35 = -10, below plain 0.
        Assert.True(_scorer.Score("밤편지 MR 라이브") < _scorer.Score("밤편지"));
    }

    [Fact]
    public void Score_NullOrEmptyTitle_ReturnsZero()
    {
        Assert.Equal(0, _scorer.Score(null));
        Assert.Equal(0, _scorer.Score(""));
    }

    [Fact]
    public void RankAndDedupe_SortsMrFirst_KeepsOriginalOrderWithinEqualScore()
    {
        var items = new[]
        {
            Item("a", "밤편지 가사"),          // penalty
            Item("b", "밤편지 노래방 MR"),      // +50
            Item("c", "밤편지"),               // 0
            Item("d", "밤편지 반주"),           // +25
        };

        IReadOnlyList<SearchVideoItem> ranked = _scorer.RankAndDedupe(items, 10);

        Assert.Equal(new[] { "b", "d", "c", "a" }, ranked.Select(i => i.VideoId));
    }

    [Fact]
    public void RankAndDedupe_DeduplicatesByVideoId()
    {
        var items = new[]
        {
            Item("dup", "밤편지 노래방"),
            Item("other", "밤편지"),
            Item("dup", "밤편지 노래방 (재업로드)"),
        };

        IReadOnlyList<SearchVideoItem> ranked = _scorer.RankAndDedupe(items, 10);

        Assert.Equal(2, ranked.Count);
        Assert.Equal(new[] { "dup", "other" }, ranked.Select(i => i.VideoId));
    }

    [Fact]
    public void RankAndDedupe_CapsToMaxResults()
    {
        var items = Enumerable.Range(0, 10).Select(i => Item(i.ToString(), $"곡{i}")).ToArray();
        IReadOnlyList<SearchVideoItem> ranked = _scorer.RankAndDedupe(items, 3);
        Assert.Equal(3, ranked.Count);
    }
}
