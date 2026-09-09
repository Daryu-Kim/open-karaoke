namespace OpenKaraoke.Core.Search;

/// <summary>A single YouTube video surfaced by the search pipeline.</summary>
public sealed record SearchVideoItem
{
    public required string VideoId { get; init; }
    public required string Title { get; init; }
    public required string ChannelTitle { get; init; }

    public string ThumbnailUrl { get; init; } = string.Empty;

    /// <summary>Seconds, 0 when the video detail lookup was skipped/failed.</summary>
    public int DurationSeconds { get; init; }

    public bool Embeddable { get; init; } = true;
}

/// <summary>Result of a search request, including whether it came from cache.</summary>
public sealed record SearchResponse
{
    public required IReadOnlyList<SearchVideoItem> Items { get; init; }
    public required string QueryUsed { get; init; }
    public bool FromCache { get; init; }
}
