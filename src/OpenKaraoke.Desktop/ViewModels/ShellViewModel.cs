using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenKaraoke.Core.Audio;
using OpenKaraoke.Core.Configuration;
using OpenKaraoke.Core.Download;
using OpenKaraoke.Core.Formatting;
using OpenKaraoke.Core.Library;
using OpenKaraoke.Core.Search;
using OpenKaraoke.Core.Time;

namespace OpenKaraoke.Desktop.ViewModels;

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

    /// <summary>Upcoming songs (대기곡). The currently playing song is not stored here.</summary>
    public ObservableCollection<QueueItemViewModel> Queue { get; } = new();

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
        _player = player ?? KaraokePlayerFactory.Create();
        _player.PlaybackEnded += OnPlayerPlaybackEnded;

        _ticker = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _ticker.Tick += OnTickerTick;
        _ticker.Start();

        Queue.CollectionChanged += OnQueueChanged;

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

    /// <summary>Queue size label on the 대기곡 panel header, e.g. "3곡".</summary>
    [ObservableProperty]
    private string _queueCountText = "0곡";

    /// <summary>True when at least one song is waiting; toggles the queue list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoQueueSongs))]
    [NotifyPropertyChangedFor(nameof(CanTogglePlay))]
    private bool _hasQueueSongs;

    /// <summary>Inverse of HasQueueSongs; shows the empty-state panel.</summary>
    public bool HasNoQueueSongs => !HasQueueSongs;

    /// <summary>True while the engine is actually producing sound.</summary>
    [ObservableProperty]
    private bool _isPlaying;

    /// <summary>Inverse of IsPlaying; swaps the play/pause vector icon.</summary>
    public bool IsPaused => !IsPlaying;

    partial void OnIsPlayingChanged(bool value) => OnPropertyChanged(nameof(IsPaused));

    /// <summary>True when a track is open and the play button can act.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanTogglePlay))]
    private bool _hasTrack;

    /// <summary>Play button is enabled while a track is open or a song is queued.</summary>
    public bool CanTogglePlay => HasTrack || HasQueueSongs;

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
                OnPropertyChanged(nameof(KeyText));
            }
        }
    }

    /// <summary>Key shift text on the PlayerBar, e.g. "2 반음" / "-1 반음".</summary>
    public string KeyText => $"{KeySemitones} 반음";

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

    /// <summary>Play/pause toggle. With an open track it toggles playback; when idle it starts the first queued song.</summary>
    [RelayCommand]
    private async Task TogglePlayAsync()
    {
        if (HasTrack && _player.IsOpen)
        {
            if (_player.State == PlayerState.Playing)
            {
                _player.Pause();
                IsPlaying = false;
            }
            else
            {
                IsPlaying = _player.Play();
            }

            return;
        }

        if (Queue.Count > 0)
        {
            await StartNextQueuedAsync();
        }
    }

    /// <summary>
    /// Adds a song to the queue. When nothing is playing the song starts
    /// immediately (예약형 model: 곡 선택 = 대기곡에 추가, 순서대로 자동 재생).
    /// </summary>
    [RelayCommand]
    private void EnqueueSong(SongItemViewModel? song)
    {
        if (song is null)
        {
            return;
        }

        if (!File.Exists(song.LocalPath))
        {
            NowPlayingTitle = "파일을 찾을 수 없습니다";
            NowPlayingSubtitle = song.Title;
            return;
        }

        if (_player.IsOpen && _player.State != PlayerState.Stopped)
        {
            Queue.Add(new QueueItemViewModel(song));
        }
        else
        {
            _ = PlayTrackAsync(song);
        }
    }

    [RelayCommand]
    private void RemoveQueueItem(QueueItemViewModel? item)
    {
        if (item is not null)
        {
            Queue.Remove(item);
        }
    }

    private bool CanClearQueue() => HasQueueSongs;

    [RelayCommand(CanExecute = nameof(CanClearQueue))]
    private void ClearQueue()
    {
        Queue.Clear();
    }

    private bool CanGoPrev() => HasTrack;

    /// <summary>Restarts the current song from the beginning.</summary>
    [RelayCommand(CanExecute = nameof(CanGoPrev))]
    private void Prev()
    {
        if (_player.IsOpen)
        {
            _player.Seek(TimeSpan.Zero);
        }
    }

    private bool CanSkip() => HasTrack || HasQueueSongs;

    /// <summary>Skips to the next queued song; with an empty queue it stops playback.</summary>
    [RelayCommand(CanExecute = nameof(CanSkip))]
    private async Task NextAsync()
    {
        if (Queue.Count > 0)
        {
            await StartNextQueuedAsync();
        }
        else if (_player.IsOpen)
        {
            _player.Stop();
            IsPlaying = false;
        }
    }

    /// <summary>Pops the first queued song and plays it.</summary>
    private async Task StartNextQueuedAsync()
    {
        if (Queue.Count == 0)
        {
            return;
        }

        var next = Queue[0];
        Queue.RemoveAt(0);
        await PlayTrackAsync(next.Song);
    }

    /// <summary>Opens a song and starts playback; also re-applies key/tempo.</summary>
    private async Task PlayTrackAsync(SongItemViewModel song)
    {
        bool opened = await _player.OpenAsync(song.LocalPath);
        if (!opened)
        {
            NowPlayingTitle = "이 곡을 재생할 수 없습니다";
            NowPlayingSubtitle = BuildOpenFailureSubtitle(song);
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

    /// <summary>
    /// Shows why a song could not be played. The Linux engine (ffmpeg/OpenAL) reports a
    /// Korean reason through <see cref="IKaraokePlayer.LastError"/>; the Windows engine
    /// does not, so the song title alone is used there.
    /// </summary>
    private string BuildOpenFailureSubtitle(SongItemViewModel song)
    {
        string? reason = _player.LastError;
        return string.IsNullOrWhiteSpace(reason) ? song.Title : $"{song.Title} · {reason}";
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
        if (Dispatcher.UIThread.CheckAccess())
        {
            OnTrackEnded();
        }
        else
        {
            Dispatcher.UIThread.Post(OnTrackEnded);
        }
    }

    /// <summary>Natural end of the current song: start the next queued song or return to idle.</summary>
    private void OnTrackEnded()
    {
        if (Queue.Count > 0)
        {
            _ = StartNextQueuedAsync();
            return;
        }

        IsPlaying = false;
        HasTrack = false;
        NowPlayingTitle = "재생할 곡을 선택하세요";
        NowPlayingSubtitle = string.Empty;
        CurrentTimeText = MediaTimeFormatter.Format(TimeSpan.Zero);
        ProgressFraction = 0;
    }

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        QueueCountText = $"{Queue.Count}곡";
        HasQueueSongs = Queue.Count > 0;
        QueueStatusText = Queue.Count > 0 ? "위에서부터 순서대로 재생됩니다" : "대기곡이 없습니다";
    }

    partial void OnHasTrackChanged(bool value)
    {
        PrevCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasQueueSongsChanged(bool value)
    {
        NextCommand.NotifyCanExecuteChanged();
        ClearQueueCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _ticker.Stop();
        _ticker.Tick -= OnTickerTick;
        Queue.CollectionChanged -= OnQueueChanged;
        _player.PlaybackEnded -= OnPlayerPlaybackEnded;
        if (_ownsPlayer)
        {
            _player.Dispose();
        }
    }
}
