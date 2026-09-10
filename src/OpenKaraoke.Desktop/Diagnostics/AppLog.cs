using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Logging;
using Avalonia.Threading;
using OpenKaraoke.Core.Audio;
using OpenKaraoke.Core.Configuration;
using OpenKaraoke.Core.Download;

namespace OpenKaraoke.Desktop.Diagnostics;

/// <summary>
/// Bridges Avalonia's log events (XAML/binding/render warnings), startup environment details and
/// unhandled exceptions into a plain text file so a store PC can be diagnosed without a debugger.
/// </summary>
public static class AppLog
{
    /// <summary>Log file written next to the executable unless <see cref="LogFilePath"/> is set first.</summary>
    public const string DefaultRelativePath = "logs/app.log";

    /// <summary>Rotated once it passes this size so the log can never fill a store PC disk.</summary>
    private const long MaxLogBytes = 2 * 1024 * 1024;

    private static readonly object Gate = new();
    private static bool _initialized;
    private static int _uiThreadHandlerAttached;

    /// <summary>File that receives every warning/error the app logs.</summary>
    public static string LogFilePath { get; set; } = string.Empty;

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(LogFilePath))
            {
                LogFilePath = Path.Combine(AppContext.BaseDirectory, DefaultRelativePath);
            }

            Logger.Sink = new FileLoggerSink();
            AttachProcessHandlers();
            _initialized = true;
        }
    }

    /// <summary>
    /// Writes one diagnostic block at startup (version, folders, external tools, API key presence)
    /// so most support questions can be answered from the log alone.
    /// </summary>
    public static void WriteStartupInfo()
    {
        Write($"=== OpenKaraoke {typeof(AppLog).Assembly.GetName().Version} ===");
        Write($"os: {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})");
        Write($"base: {AppContext.BaseDirectory}");
        Write($"log: {LogFilePath}");
        Write($"ffmpeg: {Describe(FfmpegLocator.Resolve)}");
        Write($"yt-dlp: {Describe(() => YtDlpProcessRunner.ResolveExecutable(null))}");
        Write($"youtube api key: {ApiKeyState()}");
    }

    /// <summary>Writes one line to the log file (best effort; never throws at the caller).</summary>
    public static void Write(string message)
    {
        Debug.WriteLine(message);
        if (string.IsNullOrWhiteSpace(LogFilePath))
        {
            return;
        }

        try
        {
            string? directory = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            RotateIfTooLarge();

            File.AppendAllText(
                LogFilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Logging must never take the app down.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void AttachProcessHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Write($"[FATAL] {args.ExceptionObject}");

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Write($"[TASK] {args.Exception}");
            args.SetObserved();
        };
    }

    /// <summary>
    /// Hooks the UI thread's unhandled-exception event. Call this only once Avalonia's platform
    /// services are up: touching <see cref="Dispatcher.UIThread"/> earlier caches a dispatcher
    /// without a platform main loop, which makes the app die at startup with
    /// <c>PlatformNotSupportedException</c>.
    /// </summary>
    public static void AttachUiThreadHandler()
    {
        if (Interlocked.Exchange(ref _uiThreadHandlerAttached, 1) != 0)
        {
            return;
        }

        // A failed click must not stop the music on a store PC: log it and keep running.
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            Write($"[UI] {args.Exception}");
            args.Handled = true;
        };

        // False here means the platform loop is missing and every window would freeze.
        Write($"dispatcher main loop: {Dispatcher.UIThread.SupportsRunLoops}");
    }

    private static void RotateIfTooLarge()
    {
        var info = new FileInfo(LogFilePath);
        if (!info.Exists || info.Length < MaxLogBytes)
        {
            return;
        }

        File.Move(LogFilePath, LogFilePath + ".1", overwrite: true);
    }

    private static string ApiKeyState()
        => string.IsNullOrWhiteSpace(AppSettings.GetYoutubeApiKey()) ? "missing" : "configured";

    private static string Describe(Func<string> resolve)
    {
        try
        {
            string path = resolve();
            return File.Exists(path) ? path : $"{path} (not found)";
        }
        catch (Exception ex)
        {
            return $"unknown ({ex.GetType().Name})";
        }
    }

    private sealed class FileLoggerSink : ILogSink
    {
        public bool IsEnabled(LogEventLevel level, string area)
            => level >= LogEventLevel.Warning;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
            => Write($"[{level}] {area}: {messageTemplate}");

        public void Log(
            LogEventLevel level,
            string area,
            object? source,
            string messageTemplate,
            params object?[] propertyValues)
        {
            string rendered = messageTemplate;
            for (int i = 0; i < propertyValues.Length; i++)
            {
                rendered += $" | arg{i}={propertyValues[i]}";
            }

            Write($"[{level}] {area}: {rendered}");
        }
    }
}
