using System.Diagnostics;
using OpenKaraoke.Core.Configuration;

namespace OpenKaraoke.Core.Download;

/// <summary>Result of one yt-dlp audio download attempt.</summary>
public sealed record YtDlpDownloadResult(
    bool Success,
    string? OutputFilePath,
    string? ErrorMessage,
    double? FinalProgressPercent);

/// <summary>
/// Thin seam around the yt-dlp process so the download service can be unit-tested.
/// </summary>
public interface IYtDlpRunner
{
    /// <summary>
    /// Downloads the best single audio stream of <paramref name="videoId"/> into
    /// <paramref name="outputDirectory"/>, streaming progress through <paramref name="progress"/>.
    /// </summary>
    Task<YtDlpDownloadResult> DownloadAudioAsync(
        string videoId,
        string outputDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Default implementation that shells out to the yt-dlp executable.</summary>
public sealed class YtDlpProcessRunner : IYtDlpRunner
{
    private readonly string _ytDlpPath;
    private readonly bool _toolPresent;

    /// <summary>
    /// Creates a runner for the given yt-dlp executable path. Pass null to auto-detect
    /// (searches the app base directory, then the PATH).
    /// </summary>
    public YtDlpProcessRunner(string? ytDlpPath = null)
    {
        _ytDlpPath = ResolveExecutable(ytDlpPath);
        _toolPresent = File.Exists(_ytDlpPath);
    }

    /// <summary>Whether a usable yt-dlp executable could be located.</summary>
    public bool IsAvailable => _toolPresent;

    /// <summary>
    /// Resolves yt-dlp, falling back to the path configured in the 설정 screen when no
    /// explicit path is given (searches the app base directory, then the PATH).
    /// </summary>
    public static string ResolveExecutable(string? explicitPath)
        => Core.Platform.ExecutableLocator.Resolve(
            explicitPath ?? AppSettings.GetYtDlpPath(),
            "yt-dlp");

    public async Task<YtDlpDownloadResult> DownloadAudioAsync(
        string videoId,
        string outputDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return new YtDlpDownloadResult(false, null, "영상 ID가 없습니다.", null);
        }

        if (!_toolPresent)
        {
            return new YtDlpDownloadResult(false, null, YtDlpErrors.ToolMissingMessage, null);
        }

        Directory.CreateDirectory(outputDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = _ytDlpPath,
            WorkingDirectory = outputDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in YtDlpArguments.Build(videoId, outputDirectory))
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        var tracker = new YtDlpOutputTracker();

        try
        {
            if (!process.Start())
            {
                return new YtDlpDownloadResult(false, null, YtDlpErrors.ToolMissingMessage, null);
            }
        }
        catch (Exception)
        {
            return new YtDlpDownloadResult(false, null, YtDlpErrors.ToolMissingMessage, null);
        }

        Task stdoutTask = ReadLinesAsync(process.StandardOutput, tracker, cancellationToken);
        Task stderrTask = ReadLinesAsync(process.StandardError, null, cancellationToken);

        // Kill the child process when the caller cancels so no orphan download keeps running.
        using CancellationTokenRegistration killRegistration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited.
            }
            catch (Exception)
            {
                // Best-effort kill; the WaitForExitAsync token still unblocks the caller.
            }
        });

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            return new YtDlpDownloadResult(false, null, YtDlpErrors.TimeoutOrCancelledMessage, null);
        }

        if (tracker.Completed && tracker.DestinationPath != null && File.Exists(tracker.DestinationPath))
        {
            return new YtDlpDownloadResult(true, tracker.DestinationPath, null, tracker.LastProgressPercent);
        }

        // Fallback: locate the file by its deterministic template prefix.
        string? found = LocateByVideoId(outputDirectory, videoId);
        if (found != null)
        {
            return new YtDlpDownloadResult(true, found, null, tracker.LastProgressPercent);
        }

        string? rawError = tracker.Errors.Count > 0 ? tracker.Errors[^1] : null;
        if (cancellationToken.IsCancellationRequested)
        {
            return new YtDlpDownloadResult(false, null, YtDlpErrors.TimeoutOrCancelledMessage, null);
        }

        return new YtDlpDownloadResult(false, null, YtDlpErrors.ToKorean(rawError, false), null);
    }

    private static string? LocateByVideoId(string outputDirectory, string videoId)
    {
        try
        {
            string prefix = Path.Combine(outputDirectory, videoId + ".");
            return Directory.EnumerateFiles(outputDirectory)
                .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task ReadLinesAsync(
        StreamReader reader,
        YtDlpOutputTracker? tracker,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            tracker?.ProcessLine(line);
        }
    }
}
