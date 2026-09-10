using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using OpenKaraoke.App.ViewModels;

namespace OpenKaraoke.App;

/// <summary>
/// Main kiosk window. Owns fullscreen state (UI/chrome concern); the shell
/// view model exposes the toggle command and the observable state.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private WindowState _restoreState = WindowState.Normal;
    private WindowStyle _restoreStyle = WindowStyle.SingleBorderWindow;
    private bool _isFullScreen;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new ShellViewModel();
        DataContext = _viewModel;
        _viewModel.FullscreenRequested += OnFullscreenRequested;
        _viewModel.Search.DownloadRequested += OnDownloadRequested;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _viewModel.Search.DownloadRequested -= OnDownloadRequested;
        _viewModel.FullscreenRequested -= OnFullscreenRequested;
        _viewModel.Dispose();
    }

    private void OnFullscreenRequested(object? sender, EventArgs e) => ToggleFullScreen();

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"라이브러리를 여는 중 오류가 발생했습니다.\n\n{ex.Message}",
                "오류", MessageBoxButton.OK, MessageBoxImage.Error);
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

        var dialog = new DownloadDialog(download) { Owner = this };
        if (dialog.ShowDialog() == true && download.SavedSong != null)
        {
            _ = _viewModel.Library.LoadCommand.ExecuteAsync(null);
        }
    }

    private async void DeleteSong_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SongItemViewModel item } button)
        {
            return;
        }

        MessageBoxResult choice = MessageBox.Show(this,
            $"“{item.Title}” 곡을 라이브러리에서 삭제할까요?\n로컬에 저장된 음원 파일도 함께 삭제됩니다.",
            "곡 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (choice == MessageBoxResult.Yes)
        {
            await _viewModel.Library.DeleteSongAsync(item);
        }
    }

    /// <summary>Double-clicking a library row enqueues the song (예약).</summary>
    private void LibrarySongsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LibrarySongsList.SelectedItem is SongItemViewModel item
            && _viewModel.EnqueueSongCommand.CanExecute(item))
        {
            _viewModel.EnqueueSongCommand.Execute(item);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool typing = e.OriginalSource is TextBoxBase;

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.None && !typing)
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

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.IsRepeat && _viewModel.Search.SearchCommand.CanExecute(null))
        {
            _viewModel.Search.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            WindowStyle = _restoreStyle;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _restoreState;
        }
        else
        {
            _restoreState = WindowState;
            _restoreStyle = WindowStyle;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }

        _isFullScreen = !_isFullScreen;
        _viewModel.IsFullScreen = _isFullScreen;
    }
}
