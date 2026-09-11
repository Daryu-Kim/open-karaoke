using OpenKaraoke.Core.Formatting;
using OpenKaraoke.Core.Library;
using OpenKaraoke.Core.Video;

namespace OpenKaraoke.Desktop.ViewModels;

/// <summary>Display model for one row of the local library list.</summary>
public sealed class SongItemViewModel
{
    public SongItemViewModel(SongRecord song)
    {
        Id = song.Id;
        VideoId = song.VideoId;
        Title = song.Title;
        Artist = song.Artist;
        TjNumber = song.TjNumber;
        LocalPath = song.LocalPath;
        DurationSeconds = song.DurationSeconds;
        DurationText = song.DurationSeconds > 0
            ? MediaTimeFormatter.Format(TimeSpan.FromSeconds(song.DurationSeconds))
            : string.Empty;
        ThumbnailUrl = song.ThumbnailUrl ?? string.Empty;
    }

    public long Id { get; }
    public string VideoId { get; }
    public string Title { get; }
    public string Artist { get; }
    public string? TjNumber { get; }
    public string LocalPath { get; }
    public int DurationSeconds { get; }
    public string DurationText { get; }
    public string ThumbnailUrl { get; }

    /// <summary>Text shown in the TJ 번호 column; placeholder when the owner did not type one.</summary>
    public string TjDisplay => string.IsNullOrWhiteSpace(TjNumber) ? "—" : TjNumber;

    public bool HasTjNumber => !string.IsNullOrWhiteSpace(TjNumber);

    /// <summary>Avalonia binds IsVisible directly, so the empty-length check lives here.</summary>
    public bool HasDuration => DurationText.Length > 0;

    /// <summary>
    /// True when the stored file carries a video stream. Songs downloaded before 가사 화면 support
    /// are audio only; those rows offer "영상 다시 받기". Decided from the file extension so no
    /// database column is needed.
    /// </summary>
    public bool HasVideoFile => VideoFiles.IsVideoContainer(LocalPath);

    /// <summary>Shows the 영상 다시 받기 button on rows that only have sound.</summary>
    public bool CanRedownloadVideo => !HasVideoFile;
}
