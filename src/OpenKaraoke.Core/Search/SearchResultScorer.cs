namespace OpenKaraoke.Core.Search;

/// <summary>
/// Ranks and deduplicates raw search results. Prefers MR/karaoke-style content when
/// the user typed a plain song query, and pushes down covers/lyrics/live re-uploads.
/// </summary>
public sealed class SearchResultScorer
{
    private static readonly string[] MrMarkers =
    {
        "노래방", "mr", "반주", "instrumental", "inst",
    };

    private static readonly string[] PenaltyMarkers =
    {
        "가사", "lyrics", "커버", "cover", "무대", "라이브", "live", "뮤비", "mv",
        "공연", "dance", "댄스", "연습", "튜토리얼", "오디션", "reaction", "리액션",
    };

    private const int MrMarkerScore = 25;
    private const int PenaltyMarkerScore = 35;

    public IReadOnlyList<SearchVideoItem> RankAndDedupe(IEnumerable<SearchVideoItem> items, int maxResults)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ranked = new List<(int Score, int Order, SearchVideoItem Item)>();

        int order = 0;
        foreach (SearchVideoItem item in items)
        {
            if (string.IsNullOrWhiteSpace(item.VideoId) || !seen.Add(item.VideoId))
            {
                continue;
            }

            ranked.Add((Score(item.Title), order++, item));
        }

        return ranked
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Order)
            .Take(maxResults)
            .Select(r => r.Item)
            .ToArray();
    }

    public int Score(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return 0;
        }

        string lower = title.ToLowerInvariant();
        int score = 0;

        foreach (string marker in MrMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
            {
                score += MrMarkerScore;
            }
        }

        foreach (string marker in PenaltyMarkers)
        {
            if (lower.Contains(marker, StringComparison.Ordinal))
            {
                score -= PenaltyMarkerScore;
            }
        }

        return score;
    }
}
