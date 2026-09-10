using System.Diagnostics;
using Avalonia.Logging;

namespace OpenKaraoke.Desktop.Diagnostics;

/// <summary>
/// Bridges Avalonia's log events (XAML/binding/render warnings) into <see cref="Debug"/> output
/// and, when <see cref="LogFilePath"/> is set, into a plain text file so a store PC can be
/// diagnosed without a debugger. Binding failures are the main thing we care about here.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static bool _initialized;

    /// <summary>Optional file that receives every warning/error Avalonia raises.</summary>
    public static string LogFilePath { get; set; } = string.Empty;

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            Logger.Sink = new FileLoggerSink();
            _initialized = true;
        }
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
