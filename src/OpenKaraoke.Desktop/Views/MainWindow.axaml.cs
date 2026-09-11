using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using OpenKaraoke.Core.Configuration;
using OpenKaraoke.Core.Tools;
using OpenKaraoke.Desktop.Diagnostics;
using OpenKaraoke.Desktop.ViewModels;

namespace OpenKaraoke.Desktop.Views;

/// <summary>
/// Main kiosk window. Owns fullscreen state (UI/chrome concern); the shell
/// view model exposes the toggle command and the observable state.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
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

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Opened += MainWindow_Opened;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _viewModel.Search.DownloadRequested -= OnDownloadRequested;
        _viewModel.SettingsRequested -= OnSettingsRequested;
        _viewModel.FullscreenRequested -= OnFullscreenRequested;
        _viewModel.Dispose();
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

    private async Task ShowDownloadDialogAsync(DownloadViewModel download)
    {
        var dialog = new DownloadDialog(download);
        bool saved = await dialog.ShowDialog<bool>(this);
        if (saved && download.SavedSong != null)
        {
            await _viewModel.Library.LoadCommand.ExecuteAsync(null);
        }
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
        }

        _isFullScreen = !_isFullScreen;
        _viewModel.IsFullScreen = _isFullScreen;
    }
}
