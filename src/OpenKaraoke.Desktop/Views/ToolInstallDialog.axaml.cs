using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenKaraoke.Core.Tools;
using OpenKaraoke.Desktop.ViewModels;

namespace OpenKaraoke.Desktop.Views;

/// <summary>
/// Modal dialog that offers to download the external tools (yt-dlp, ffmpeg) into the app folder.
/// Shown by the startup check and from the 설정 dialog, so both paths use the same installer.
/// </summary>
public partial class ToolInstallDialog : Window
{
    private readonly ToolInstallViewModel _viewModel;

    public ToolInstallDialog()
        : this(new ToolInstallViewModel(new ToolInstaller()))
    {
    }

    public ToolInstallDialog(ToolInstallViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>
    /// Shows the dialog and reports what happened, so the caller can rebind the download runner
    /// or ask for a restart.
    /// </summary>
    public static async Task<ToolInstallOutcome> ShowAsync(Window owner, bool showSkipOption)
    {
        using var installer = new ToolInstaller();
        var dialog = new ToolInstallDialog(new ToolInstallViewModel(installer, showSkipOption));
        await dialog.ShowDialog<bool>(owner);

        return new ToolInstallOutcome(dialog._viewModel.Installed, dialog._viewModel.RestartRequired);
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

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        // Closing mid-download (title-bar X) must stop the installer before its client is disposed.
        if (_viewModel.IsBusy)
        {
            _viewModel.CancelCommand.Execute(null);
        }

        _viewModel.PersistSkipChoice();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close(_viewModel.Installed);
}
