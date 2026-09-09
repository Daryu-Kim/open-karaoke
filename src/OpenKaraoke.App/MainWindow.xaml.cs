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
        _viewModel.FullscreenRequested += (_, _) => ToggleFullScreen();
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
