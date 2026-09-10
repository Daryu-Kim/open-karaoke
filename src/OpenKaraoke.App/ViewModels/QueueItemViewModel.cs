namespace OpenKaraoke.App.ViewModels;

/// <summary>
/// One pending (대기) song in the playback queue. Wraps a library item so the
/// queue list can use its own DataTemplate instead of the library row template.
/// </summary>
public sealed class QueueItemViewModel
{
    public QueueItemViewModel(SongItemViewModel song)
    {
        Song = song;
    }

    public SongItemViewModel Song { get; }

    public long Id => Song.Id;
    public string Title => Song.Title;
    public string Artist => Song.Artist;
    public string LocalPath => Song.LocalPath;
    public bool HasTjNumber => Song.HasTjNumber;
    public string TjDisplay => Song.TjDisplay;
}
