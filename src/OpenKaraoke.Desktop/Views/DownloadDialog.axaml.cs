using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using OpenKaraoke.Desktop.ViewModels;

namespace OpenKaraoke.Desktop.Views;

/// <summary>
/// Modal dialog that downloads one MR track and stores its metadata. Closes with
/// <c>true</c> shortly after the download succeeds so the shell can refresh the library.
/// </summary>
public partial class DownloadDialog : Window
{
    private readonly DownloadViewModel? _viewModel;
    private readonly DispatcherTimer _closeTimer;

    public DownloadDialog()
    {
        InitializeComponent();
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
    }

    public DownloadDialog(DownloadViewModel viewModel)
        : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _closeTimer.Tick += OnCloseTimerTick;
    }

    private void OnCloseTimerTick(object? sender, EventArgs e)
    {
        _closeTimer.Stop();
        Close(true);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadViewModel.Phase) && _viewModel!.Phase == DownloadPhase.Success)
        {
            _closeTimer.Start();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            if (_viewModel.IsBusy)
            {
                _viewModel.CancelCommand.Execute(null);
            }
        }

        _closeTimer.Stop();
        base.OnClosing(e);
    }
}
