using Avalonia;
using Avalonia.Media;
using OpenKaraoke.Desktop.Diagnostics;

namespace OpenKaraoke.Desktop;

internal static class Program
{
    // Avalonia needs an STA thread on Windows for windowing/COM interop.
    [STAThread]
    public static void Main(string[] args)
    {
        AppLog.Initialize();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Nothing else is left to report the failure, so it must reach the log file.
            AppLog.Write($"[FATAL] 시작 실패: {ex}");
            throw;
        }
    }

    /// <summary>
    /// Entry point shared by the app and the headless UI tests so both always use the
    /// same fonts, theme and rendering backends.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                // Windows keeps the Malgun Gothic look of the original build; other
                // platforms use their system default and fall back to a CJK font so
                // Korean titles/lyrics never render as empty boxes.
                DefaultFamilyName = "Malgun Gothic",
                FontFallbacks =
                [
                    new FontFallback { FontFamily = new FontFamily("Noto Sans CJK KR") },
                    new FontFallback { FontFamily = new FontFamily("Noto Sans KR") },
                    new FontFallback { FontFamily = new FontFamily("NanumGothic") },
                    new FontFallback { FontFamily = new FontFamily("Noto Color Emoji") },
                ],
            })
            .LogToTrace();
}
