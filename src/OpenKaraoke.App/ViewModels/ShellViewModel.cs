using System.ComponentModel;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKaraoke.Core.Configuration;
using OpenKaraoke.Core.Download;
using OpenKaraoke.Core.Formatting;
using OpenKaraoke.Core.Library;
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

    /// <summary>Local "내 라이브러리" sub-view model backed by the SQLite song store.</summary>
    public LibraryViewModel Library { get; }

    /// <summary>Persistent SQLite store of downloaded songs (lives under data/songs.db).</summary>
    public ISongLibraryStore SongStore { get; }

    /// <summary>Directory where downloaded MR audio files are kept (data/songs).</summary>
    public string SongsDirectory { get; }

    /// <summary>yt-dlp process wrapper used for MR downloads.</summary>
    public IYtDlpRunner DownloadRunner { get; }

    public ShellViewModel(
        ISongLibraryStore? songStore = null,
        IYtDlpRunner? downloadRunner = null)
    {
        SongsDirectory = Path.Combine(AppContext.BaseDirectory, "data", "songs");
        SongStore = songStore ?? new SqliteSongLibraryStore(
            Path.Combine(AppContext.BaseDirectory, "data", "songs.db"));
        DownloadRunner = downloadRunner ?? new YtDlpProcessRunner();

        Search = new SearchViewModel(CreateSearchService());
        Library = new LibraryViewModel(SongStore);
        Search.PropertyChanged += OnSearchPropertyChanged;
    }

    /// <summary>Opens the DB connection and loads the initial library.</summary>
    public async Task InitializeAsync()
    {
        await SongStore.InitializeAsync();
        await Library.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>Searching for a song switches the content area to the search results.</summary>
    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchViewModel.State)
            && Search.State != SearchUiState.Idle
            && IsLibraryMode)
        {
            IsLibraryMode = false;
        }
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
    [NotifyPropertyChangedFor(nameof(IsSearchMode))]
    private bool _isLibraryMode = true;

    public bool IsSearchMode => !IsLibraryMode;

    [RelayCommand]
    private void ShowLibrary() => IsLibraryMode = true;

    [RelayCommand]
    private void ShowSearch() => IsLibraryMode = false;

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
