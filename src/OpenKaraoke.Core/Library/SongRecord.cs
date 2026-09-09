namespace OpenKaraoke.Core.Library;

/// <summary>
/// One song stored in the local karaoke library. The backing audio file lives on disk at
/// <see cref="LocalPath"/>; this record holds the metadata entered at download time.
/// </summary>
public sealed record SongRecord
{
    public long Id { get; init; }

    public required string VideoId { get; init; }

    public required string Title { get; init; }

    public required string Artist { get; init; }

    /// <summary>Optional TJ karaoke song number typed by the owner when downloading.</summary>
    public string? TjNumber { get; init; }

    public required string LocalPath { get; init; }

    public int DurationSeconds { get; init; }

    public long FileSizeBytes { get; init; }

    public DateTime AddedAtUtc { get; init; }

    public string? ThumbnailUrl { get; init; }
}
