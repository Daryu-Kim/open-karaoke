using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKaraoke.Core.Audio;
using OpenKaraoke.Core.Configuration;
using OpenKaraoke.Core.Download;
using OpenKaraoke.Core.Formatting;
using OpenKaraoke.Core.Library;
using OpenKaraoke.Core.Search;
using OpenKaraoke.Core.Time;

namespace OpenKaraoke.App.ViewModels;

/// <summary>
/// Main shell state. The PlayerBar is driven live by the audio engine: a ticker
/// polls the playback position, and key/tempo changes are pushed to the engine
/// which clamps them to the TJ-style ranges (Key ±6 반음, Tempo ±20%).
/// </summary>
public partial class ShellViewModel : ObservableObject, IDisposable
{
    /// <summary>Key shift range shown on the PlayerBar, mirroring the engine clamp.</summary>
    public const int MinKeySemitones = -6;

    /// <summary>Key shift range shown on the PlayerBar, mirroring the engine clamp.</summary>
    public const int MaxKeySemitones = 6;

    /// <summary>Tempo range shown on the PlayerBar, mirroring the engine clamp.</summary>
    public const double MinTempo = 0.8;

    /// <summary>Tempo range shown on the PlayerBar, mirroring the engine clamp.</summary>
    public const double MaxTempo = 1.2;

    private const double TempoStep = 0.01;

    private readonly IKaraokePlayer _player;
    private readonly bool _ownsPlayer;
    private readonly DispatcherTimer _ticker;

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
        IYtDlpRunner? downloadRunner = null,
        IKaraokePlayer? player = null)
    {
        SongsDirectory = Path.Combine(AppContext.BaseDirectory, "data", "songs");
        SongStore = songStore ?? new SqliteSongLibraryStore(
            Path.Combine(AppContext.BaseDirectory, "data", "songs.db"));
        DownloadRunner = downloadRunner ?? new YtDlpProcessRunner();

        _ownsPlayer = player is null;
        _player = player ?? new KaraokeAudioPlayer();
        _player.PlaybackEnded += OnPlayerPlaybackEnded;

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _ticker.Tick += OnTickerTick;
        _ticker.Start();

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
    private string _queueStatusText = "대기곡이 없습니다";

    /// <summary>True while the engine is actually producing sound.</summary>
    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>True when a track is opened and the play button can act.</summary>
    [ObservableProperty]
    private bool _hasTrack;

    /// <summary>0..1 playback ratio driving the PlayerBar progress fill.</summary>
    [ObservableProperty]
    private double _progressFraction;

    private int _keySemitones;

    /// <summary>Key shift in semitones; clamped to ±6 and pushed to the engine.</summary>
    public int KeySemitones
    {
        get => _keySemitones;
        set
        {
            int clamped = Math.Clamp(value, MinKeySemitones, MaxKeySemitones);
            if (SetProperty(ref _keySemitones, clamped))
            {
                _player.KeySemitones = clamped;
            }
        }
    }

    private double _tempo = 1.0;

    /// <summary>Tempo ratio (1.0 = normal); clamped to [0.8, 1.2] and pushed to the engine.</summary>
    public double Tempo
    {
        get => _tempo;
        set
        {
            double clamped = Math.Clamp(Math.Round(value, 2), MinTempo, MaxTempo);
            if (SetProperty(ref _tempo, clamped))
            {
                _player.Tempo = clamped;
                OnPropertyChanged(nameof(TempoPercentText));
            }
        }
    }

    /// <summary>Tempo shown as a TJ-style percentage, e.g. 1.05 → "105%".</summary>
    public string TempoPercentText => $"{Tempo * 100:0}%";

    [RelayCommand]
    private void ToggleFullscreen() => FullscreenRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void KeyDown() => KeySemitones -= 1;

    [RelayCommand]
    private void KeyUp() => KeySemitones += 1;

    [RelayCommand]
    private void TempoDown() => Tempo -= TempoStep;

    [RelayCommand]
    private void TempoUp() => Tempo += TempoStep;

    /// <summary>Play/pause toggle; only acts while a track is open.</summary>
    [RelayCommand]
    private void TogglePlay()
    {
        if (!_player.IsOpen)
        {
            return;
        }

        if (_player.State == PlayerState.Playing)
        {
            _player.Pause();
            IsPlaying = false;
        }
        else
        {
            IsPlaying = _player.Play();
        }
    }

    /// <summary>Opens a library song and starts playback; used by the row's 재생 button.</summary>
    [RelayCommand]
    private async Task PlaySongAsync(SongItemViewModel? song)
    {
        if (song is null)
        {
            return;
        }

        bool opened = await _player.OpenAsync(song.LocalPath);
        if (!opened)
        {
            NowPlayingTitle = "이 곡을 재생할 수 없습니다";
            NowPlayingSubtitle = song.Title;
            HasTrack = false;
            IsPlaying = false;
            return;
        }

        // Re-apply the current key/tempo so a change made before any song was
        // opened is honored by the freshly created DSP stream.
        _player.KeySemitones = KeySemitones;
        _player.Tempo = Tempo;

        HasTrack = true;
        NowPlayingTitle = song.Title;
        NowPlayingSubtitle = BuildNowPlayingSubtitle(song);
        CurrentTimeText = MediaTimeFormatter.Format(TimeSpan.Zero);
        ProgressFraction = 0;
        IsPlaying = _player.Play();
    }

    private static string BuildNowPlayingSubtitle(SongItemViewModel song)
    {
        string subtitle = song.Artist;
        if (song.HasTjNumber)
        {
            subtitle = string.IsNullOrWhiteSpace(subtitle)
                ? $"TJ {song.TjNumber}"
                : $"{subtitle} · TJ {song.TjNumber}";
        }

        return subtitle;
    }

    /// <summary>Polls the engine and repaints time/progress/play state on the UI thread.</summary>
    private void OnTickerTick(object? sender, EventArgs e)
    {
        if (!_player.IsOpen)
        {
            return;
        }

        bool playing = _player.State == PlayerState.Playing;
        if (playing != IsPlaying)
        {
            IsPlaying = playing;
        }

        TimeSpan position = _player.Position;
        TimeSpan duration = _player.Duration;
        CurrentTimeText = MediaTimeFormatter.Format(position);

        if (duration > TimeSpan.Zero)
        {
            DurationText = MediaTimeFormatter.Format(duration);
            ProgressFraction = Math.Clamp(position.TotalSeconds / duration.TotalSeconds, 0, 1);
        }
        else
        {
            ProgressFraction = 0;
        }
    }

    /// <summary>PlaybackEnded fires on the audio thread; hop to the UI thread before touching bindings.</summary>
    private void OnPlayerPlaybackEnded(object? sender, EventArgs e)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            OnTrackEnded();
        }
        else
        {
            dispatcher.BeginInvoke(OnTrackEnded);
        }
    }

    private void OnTrackEnded()
    {
        IsPlaying = false;
        CurrentTimeText = MediaTimeFormatter.Format(TimeSpan.Zero);
        ProgressFraction = 0;
    }

    public void Dispose()
    {
        _ticker.Stop();
        _ticker.Tick -= OnTickerTick;
        _player.PlaybackEnded -= OnPlayerPlaybackEnded;
        if (_ownsPlayer)
        {
            _player.Dispose();
        }
    }
}
