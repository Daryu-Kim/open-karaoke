using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OpenKaraoke.Desktop.Diagnostics;
using OpenKaraoke.Desktop.Views;

namespace OpenKaraoke.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AppLog.Initialize();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
