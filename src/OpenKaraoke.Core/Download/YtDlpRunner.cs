using System.Diagnostics;
using OpenKaraoke.Core.Configuration;

namespace OpenKaraoke.Core.Download;

/// <summary>Result of one yt-dlp media download attempt.</summary>
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
    /// Downloads the best video+audio version of <paramref name="videoId"/> into
    /// <paramref name="outputDirectory"/>, streaming progress through <paramref name="progress"/>.
    /// </summary>
    Task<YtDlpDownloadResult> DownloadMediaAsync(
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

    /// <summary>Path of the ffmpeg that yt-dlp should use to merge video+audio; null when absent.</summary>
    private static string? ResolveFfmpegForMerge()
    {
        string path = Audio.FfmpegLocator.Resolve();
        return File.Exists(path) ? path : null;
    }

    public async Task<YtDlpDownloadResult> DownloadMediaAsync(
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
        foreach (string arg in YtDlpArguments.Build(videoId, outputDirectory, ResolveFfmpegForMerge()))
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
            List<string> candidates = Directory.EnumerateFiles(outputDirectory)
                .Where(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Prefer the final container (abc123.mp4) over intermediate parts (abc123.f137.mp4)
            // and over a leftover audio-only file (abc123.m4a) from an earlier download.
            return candidates.FirstOrDefault(f => IsFinalContainer(f, videoId))
                ?? candidates.FirstOrDefault(Video.VideoFiles.IsVideoContainer)
                ?? candidates.FirstOrDefault();
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

    private static bool IsFinalContainer(string path, string videoId) =>
        Video.VideoFiles.IsVideoContainer(path) &&
        Path.GetFileNameWithoutExtension(path).Equals(videoId, StringComparison.OrdinalIgnoreCase);

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
