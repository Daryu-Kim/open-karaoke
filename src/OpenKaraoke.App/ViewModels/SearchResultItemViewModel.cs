using OpenKaraoke.Core.Formatting;
using OpenKaraoke.Core.Search;

namespace OpenKaraoke.App.ViewModels;

/// <summary>Display model for one YouTube search result tile.</summary>
public sealed class SearchResultItemViewModel
{
    public SearchResultItemViewModel(SearchVideoItem item)
    {
        VideoId = item.VideoId;
        Title = item.Title;
        ChannelTitle = item.ChannelTitle;
        ThumbnailUrl = item.ThumbnailUrl;
        DurationText = item.DurationSeconds > 0
            ? MediaTimeFormatter.Format(TimeSpan.FromSeconds(item.DurationSeconds))
            : string.Empty;
    }

    public string VideoId { get; }
    public string Title { get; }
    public string ChannelTitle { get; }
    public string ThumbnailUrl { get; }
    public string DurationText { get; }
}
