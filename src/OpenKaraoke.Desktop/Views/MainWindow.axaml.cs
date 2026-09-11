using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.VisualTree;
using OpenKaraoke.Core.Configuration;
using OpenKaraoke.Core.Tools;
using OpenKaraoke.Desktop.Diagnostics;
using OpenKaraoke.Desktop.Media;
using OpenKaraoke.Desktop.ViewModels;

namespace OpenKaraoke.Desktop.Views;

/// <summary>
/// Main kiosk window. Owns fullscreen state (UI/chrome concern); the shell
/// view model exposes the toggle command and the observable state.
/// It also owns the karaoke screen (가사 화면): the video frames are decoded in
/// <see cref="VideoPlayback"/> and painted into either the in-app panel, the fullscreen
/// overlay, or the customer-monitor window - exactly one of them at a time.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private readonly VideoPlayback _videoPlayback;
    private VideoWindow? _videoWindow;
    private WindowState _restoreState = WindowState.Normal;
    private bool _isFullScreen;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new ShellViewModel();
        DataContext = _viewModel;
        _viewModel.FullscreenRequested += OnFullscreenRequested;
        _viewModel.SettingsRequested += OnSettingsRequested;
        _viewModel.Search.DownloadRequested += OnDownloadRequested;
        _viewModel.TrackOpened += OnTrackOpened;
        _viewModel.TrackClosed += OnTrackClosed;
        _viewModel.PropertyChanged += OnShellPropertyChanged;

        _videoPlayback = new VideoPlayback(
            () => _viewModel.PlaybackPosition,
            () => _viewModel.IsPlayingAudio);
        _videoPlayback.StateChanged += OnVideoStateChanged;

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Opened += MainWindow_Opened;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _viewModel.Search.DownloadRequested -= OnDownloadRequested;
        _viewModel.SettingsRequested -= OnSettingsRequested;
        _viewModel.FullscreenRequested -= OnFullscreenRequested;
        _viewModel.TrackOpened -= OnTrackOpened;
        _viewModel.TrackClosed -= OnTrackClosed;
        _viewModel.PropertyChanged -= OnShellPropertyChanged;
        _videoPlayback.StateChanged -= OnVideoStateChanged;
        _videoPlayback.Dispose();
        CloseVideoWindow();
        _viewModel.Dispose();
    }

    // ═══════════ 노래방 화면 (가사 영상) ═══════════

    /// <summary>Second monitor wired to the customer TV; null when the shop PC has one screen.</summary>
    private Screen? FindCustomerScreen()
    {
        Screen? primary = Screens.Primary;
        return Screens.All.FirstOrDefault(screen => !screen.IsPrimary && !ReferenceEquals(screen, primary));
    }

    private void OnTrackOpened(object? sender, string path)
    {
        _videoPlayback.Load(path);

        // Without a customer monitor the shop PC is the only screen, so show the karaoke
        // screen right away instead of waiting for a click on the tab.
        if (FindCustomerScreen() is null)
        {
            _viewModel.SetMode(library: false, video: true);
        }

        UpdateVideoTarget();
    }

    private void OnTrackClosed(object? sender, EventArgs e)
    {
        _videoPlayback.Stop();
        UpdateVideoTarget();
    }

    private void OnVideoStateChanged(object? sender, EventArgs e) => UpdateVideoTarget();

    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShellViewModel.IsVideoMode) or nameof(ShellViewModel.IsFullScreen))
        {
            UpdateVideoTarget();
        }
    }

    /// <summary>
    /// Routes the decoded frames to the customer monitor when one is attached, otherwise to the
    /// in-app karaoke panel (or the fullscreen overlay). The other surfaces stay detached, which
    /// pauses decoding and keeps a hidden screen from eating CPU.
    /// </summary>
    private void UpdateVideoTarget()
    {
        Screen? customer = FindCustomerScreen();
        string message = DescribeVideoMessage();

        if (customer is not null && _viewModel.HasTrack)
        {
            VideoWindow window = EnsureVideoWindow();
            PlaceOnCustomerScreen(window, customer);
            window.SetStatus(message);
            _videoPlayback.Attach(window.Surface);

            _viewModel.VideoStatusText = message.Length > 0
                ? message
                : "고객용 모니터에 가사 화면을 표시하고 있습니다";
            _viewModel.IsVideoStatusVisible = true;
            return;
        }

        CloseVideoWindow();

        bool panelVisible = _viewModel.IsVideoMode;
        _videoPlayback.Attach(
            panelVisible ? (_viewModel.IsFullScreen ? FullScreenVideoSurface : PanelVideoSurface) : null);

        _viewModel.VideoStatusText = message;
        _viewModel.IsVideoStatusVisible = message.Length > 0;
    }

    /// <summary>Korean status line for the karaoke screen; empty while frames are flowing.</summary>
    private string DescribeVideoMessage()
    {
        if (_videoPlayback.MediaPath is null)
        {
            return "재생할 곡을 선택하세요";
        }

        if (_videoPlayback.Message is { Length: > 0 } message)
        {
            return message;
        }

        return _videoPlayback.HasVideo ? string.Empty : VideoPlayback.NoVideoMessage;
    }

    private VideoWindow EnsureVideoWindow()
    {
        if (_videoWindow is { } existing)
        {
            if (!existing.IsVisible)
            {
                existing.Show();
            }

            return existing;
        }

        var window = new VideoWindow();
        window.Closed += OnVideoWindowClosed;
        _videoWindow = window;
        window.Show();
        return window;
    }

    /// <summary>Sizes the borderless window to the whole customer monitor.</summary>
    private static void PlaceOnCustomerScreen(VideoWindow window, Screen screen)
    {
        // A window the operator dragged stays where they put it (the escape hatch for window
        // managers that ignore programmatic placement); the size is still managed.
        if (!window.MovedByUser)
        {
            window.Position = screen.Bounds.Position;
        }

        window.Width = screen.Bounds.Width / screen.Scaling;
        window.Height = screen.Bounds.Height / screen.Scaling;
    }

    private void OnVideoWindowClosed(object? sender, EventArgs e)
    {
        if (sender is VideoWindow window)
        {
            window.Closed -= OnVideoWindowClosed;
            if (ReferenceEquals(_videoWindow, window))
            {
                _videoWindow = null;
            }
        }
    }

    private void CloseVideoWindow()
    {
        if (_videoWindow is not { } window)
        {
            return;
        }

        _videoWindow = null;
        window.Closed -= OnVideoWindowClosed;
        window.Close();
    }

    private void OnFullscreenRequested(object? sender, EventArgs e) => ToggleFullScreen();

    /// <summary>Opens the 모달 설정 dialog; saved values are re-applied without a restart.</summary>
    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        AppLog.Write("[settings] 설정 창을 엽니다");
        _ = ShowSettingsDialogAsync();
    }

    private async Task ShowSettingsDialogAsync()
    {
        try
        {
            var settings = new SettingsViewModel(_viewModel.SongsDirectory);
            var dialog = new SettingsDialog(settings);
            await dialog.ShowDialog<bool>(this);

            // Any close path (닫기 button or the window X) applies what was stored.
            if (settings.Saved)
            {
                _viewModel.ApplySettings();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"[settings] 창을 열지 못했습니다: {ex}");
            await MessageDialog.InfoAsync(this, "오류", $"설정 창을 여는 중 오류가 발생했습니다.\n\n{ex.Message}");
        }
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        Opened -= MainWindow_Opened;
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write($"[library] 초기화 실패: {ex}");
            await MessageDialog.InfoAsync(this, "오류", $"라이브러리를 여는 중 오류가 발생했습니다.\n\n{ex.Message}");
        }

        await PromptForMissingToolsAsync();
    }

    /// <summary>
    /// Startup check: when a tool this platform cannot work without is missing, offer to download
    /// it. The reminder can be switched off permanently from inside the dialog.
    /// </summary>
    private async Task PromptForMissingToolsAsync()
    {
        try
        {
            if (AppSettings.GetFlag(AppSettings.UiSection, AppSettings.SkipToolInstallPromptKey))
            {
                return;
            }

            if (!ToolCatalog.AnyRequiredMissing())
            {
                return;
            }

            AppLog.Write("[tools] 필수 도구가 없어 설치 안내를 표시합니다");
            await InstallMissingToolsAsync(showSkipOption: true);
        }
        catch (Exception ex)
        {
            AppLog.Write($"[tools] 설치 안내를 표시하지 못했습니다: {ex}");
        }
    }

    /// <summary>Runs the 설치 dialog; installed tools are applied without restarting where possible.</summary>
    private async Task InstallMissingToolsAsync(bool showSkipOption)
    {
        ToolInstallOutcome outcome = await ToolInstallDialog.ShowAsync(this, showSkipOption);

        if (outcome.Installed)
        {
            // Re-creates the yt-dlp runner so the freshly downloaded binary is used right away.
            _viewModel.ApplySettings();
        }

        if (outcome.RestartRequired)
        {
            await MessageDialog.InfoAsync(
                this,
                "앱 다시 시작 필요",
                "ffmpeg 설치가 끝났습니다.\n재생에 적용하려면 앱을 다시 시작해 주세요.");
        }
    }

    /// <summary>Opens the modal download dialog for the chosen search result.</summary>
    private void OnDownloadRequested(SearchResultItemViewModel item)
    {
        var download = new DownloadViewModel(
            _viewModel.DownloadRunner,
            _viewModel.SongStore,
            _viewModel.SongsDirectory,
            item);

        _ = ShowDownloadDialogAsync(download);
    }

    /// <summary>
    /// Re-downloads a library song that only has sound, so the customer screen can show lyrics.
    /// The song keeps its metadata (제목/가수/TJ 번호) and the old file is dropped afterwards.
    /// </summary>
    private async void RedownloadSong_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SongItemViewModel item })
        {
            return;
        }

        bool confirmed = await MessageDialog.ConfirmAsync(
            this,
            "영상 다시 받기",
            $"“{item.Title}” 곡을 가사 영상(1080p)으로 다시 받을까요?\n"
            + "용량이 커지지만 손님용 화면에 가사가 표시됩니다.");

        if (!confirmed)
        {
            return;
        }

        var download = new DownloadViewModel(
            _viewModel.DownloadRunner,
            _viewModel.SongStore,
            _viewModel.SongsDirectory,
            item);

        await ShowDownloadDialogAsync(download);
    }

    private async Task ShowDownloadDialogAsync(DownloadViewModel download)
    {
        var dialog = new DownloadDialog(download);
        bool saved = await dialog.ShowDialog<bool>(this);
        if (saved && download.SavedSong != null)
        {
            RebindVideoAfterDownload(download);
            await _viewModel.Library.LoadCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// Points the karaoke screen at the fresh file when the download replaced the song the video
    /// pipeline currently follows (영상 다시 받기 on the playing song), so the lyrics appear without
    /// restarting playback. Re-downloads of other rows leave the running video alone.
    /// </summary>
    private void RebindVideoAfterDownload(DownloadViewModel download)
    {
        if (download.SavedSong is not { } song || download.PreviousFilePath is not { } replaced)
        {
            return;
        }

        if (_videoPlayback.MediaPath is not { } loaded
            || !string.Equals(Path.GetFullPath(loaded), Path.GetFullPath(replaced), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _videoPlayback.Load(song.LocalPath);
        UpdateVideoTarget();
    }

    private async void DeleteSong_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SongItemViewModel item })
        {
            return;
        }

        bool confirmed = await MessageDialog.ConfirmAsync(
            this,
            "곡 삭제",
            $"“{item.Title}” 곡을 라이브러리에서 삭제할까요?\n로컬에 저장된 음원 파일도 함께 삭제됩니다.");

        if (confirmed)
        {
            await _viewModel.Library.DeleteSongAsync(item);
        }
    }

    /// <summary>Double-clicking a library row enqueues the song (예약).</summary>
    private void LibrarySongsList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // Ignore double taps that landed on the row's own buttons (예약 / 삭제).
        if (e.Source is Visual visual && visual.FindAncestorOfType<Button>() is not null)
        {
            return;
        }

        if (sender is ListBox { SelectedItem: SongItemViewModel item }
            && _viewModel.EnqueueSongCommand.CanExecute(item))
        {
            _viewModel.EnqueueSongCommand.Execute(item);
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        bool typing = e.Source is TextBox;

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.None && !typing)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isFullScreen)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
    }

    private void SearchTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.Search.SearchCommand.CanExecute(null))
        {
            _viewModel.Search.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            WindowState = _restoreState;
        }
        else
        {
            _restoreState = WindowState;
            WindowState = WindowState.FullScreen;

            // 전체화면 = 손님용 화면: show the karaoke screen instead of the library.
            _viewModel.SetMode(library: false, video: true);
        }

        _isFullScreen = !_isFullScreen;
        _viewModel.IsFullScreen = _isFullScreen;
    }
}
