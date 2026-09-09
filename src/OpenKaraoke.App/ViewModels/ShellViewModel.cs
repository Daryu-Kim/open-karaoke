using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKaraoke.Core.Configuration;
using OpenKaraoke.Core.Formatting;
using OpenKaraoke.Core.Search;
using OpenKaraoke.Core.Time;

namespace OpenKaraoke.App.ViewModels;

/// <summary>
/// Main shell state. Audio-engine driven values (time, key, tempo) receive real
/// updates starting in M4/M5; they are initialized here for the PlayerBar layout.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    public event EventHandler? FullscreenRequested;

    /// <summary>YouTube search sub-view model; the API key is read from local config.</summary>
    public SearchViewModel Search { get; }

    public ShellViewModel()
    {
        Search = new SearchViewModel(CreateSearchService());
    }

    private static YoutubeSearchService CreateSearchService()
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri("https://www.googleapis.com/"),
            Timeout = TimeSpan.FromSeconds(15),
        };

        string? apiKey = AppSettings.GetYoutubeApiKey();
        return new YoutubeSearchService(http, apiKey ?? string.Empty, SystemClock.Instance);
    }

    [ObservableProperty]
    private bool _isFullScreen;

    [ObservableProperty]
    private string _nowPlayingTitle = "재생할 곡을 선택하세요";

    [ObservableProperty]
    private string _nowPlayingSubtitle = "";

    [ObservableProperty]
    private string _currentTimeText = MediaTimeFormatter.Format(TimeSpan.Zero);

    [ObservableProperty]
    private string _durationText = MediaTimeFormatter.Format(TimeSpan.Zero);

    [ObservableProperty]
    private int _keySemitones;

    [ObservableProperty]
    private double _tempo = 1.0;

    [ObservableProperty]
    private string _queueStatusText = "대기곡이 없습니다";

    [RelayCommand]
    private void ToggleFullscreen() => FullscreenRequested?.Invoke(this, EventArgs.Empty);
}
