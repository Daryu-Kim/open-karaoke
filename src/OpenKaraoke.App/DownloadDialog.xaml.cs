using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using OpenKaraoke.App.ViewModels;

namespace OpenKaraoke.App;

public partial class DownloadDialog : Window
{
    private readonly DownloadViewModel _viewModel;
    private readonly DispatcherTimer _closeTimer;

    public DownloadDialog(DownloadViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            DialogResult = true;
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DownloadViewModel.Phase) && _viewModel.Phase == DownloadPhase.Success)
        {
            _closeTimer.Start();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _closeTimer.Stop();
        if (_viewModel.IsBusy)
        {
            _viewModel.CancelCommand.Execute(null);
        }
        base.OnClosing(e);
    }
}
