using Avalonia;
using Avalonia.Media;

namespace OpenKaraoke.Desktop;

internal static class Program
{
    // Avalonia needs an STA thread on Windows for windowing/COM interop.
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

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
