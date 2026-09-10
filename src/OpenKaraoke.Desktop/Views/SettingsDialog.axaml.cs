using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenKaraoke.Desktop.Diagnostics;
using OpenKaraoke.Desktop.ViewModels;

namespace OpenKaraoke.Desktop.Views;

/// <summary>
/// Modal dialog behind the header 설정 button. Closes with <c>true</c> when the user saved
/// something, so the shell can re-apply the API key and the tool paths without a restart.
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsDialog()
        : this(new SettingsViewModel(Path.Combine(AppContext.BaseDirectory, "data", "songs")))
    {
    }

    public SettingsDialog(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Kiosk displays can be shorter than the content, so cap the height and let it scroll.
        if (Screens.ScreenFromWindow(this) is { } screen)
        {
            MaxHeight = Math.Max(480, screen.WorkingArea.Height / screen.Scaling * 0.92);
        }
    }

    private void Save_Click(object? sender, RoutedEventArgs e) => _viewModel.Save();

    private void Close_Click(object? sender, RoutedEventArgs e) => Close(_viewModel.Saved);

    private async void BrowseFfmpeg_Click(object? sender, RoutedEventArgs e)
    {
        string? path = await PickExecutableAsync("ffmpeg 실행 파일 선택");
        if (path is { Length: > 0 })
        {
            _viewModel.FfmpegPath = path;
        }
    }

    private async void BrowseYtDlp_Click(object? sender, RoutedEventArgs e)
    {
        string? path = await PickExecutableAsync("yt-dlp 실행 파일 선택");
        if (path is { Length: > 0 })
        {
            _viewModel.YtDlpPath = path;
        }
    }

    private async Task<string?> PickExecutableAsync(string title)
    {
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = title,
                    AllowMultiple = false,
                });

            if (files.Count == 0)
            {
                return null;
            }

            return files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
        }
        catch (Exception ex)
        {
            // Some Linux sessions have no portal for native dialogs; the path box stays usable.
            AppLog.Write($"[settings] 파일 선택 창을 열지 못했습니다: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
