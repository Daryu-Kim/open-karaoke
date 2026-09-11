using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenKaraoke.Core.Video;

/// <summary>
/// Reads the stream layout of a media file by asking ffmpeg to describe it
/// (<c>ffmpeg -i file</c> prints the stream table to stderr and exits with code 1).
/// Results are cached per file so switching songs never re-runs the probe.
/// </summary>
public static partial class FfmpegMediaProbe
{
    private const int ProbeTimeoutMs = 10_000;

    /// <summary>Assumed output rate when ffmpeg does not report one (the reader forces this rate anyway).</summary>
    private const double FallbackFrameRate = 30;

    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\b(\d{2,5})x(\d{2,5})\b", RegexOptions.CultureInvariant)]
    private static partial Regex FrameSizeRegex();

    [GeneratedRegex(@"([\d.]+)\s*fps\b", RegexOptions.CultureInvariant)]
    private static partial Regex FrameRateRegex();

    /// <summary>
    /// Probes <paramref name="filePath"/>, returning <see cref="VideoMediaInfo.None"/> when the file
    /// has no usable video stream or ffmpeg is missing.
    /// </summary>
    public static VideoMediaInfo Probe(string filePath, string? ffmpegPath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return VideoMediaInfo.None;
        }

        string resolved = string.IsNullOrWhiteSpace(ffmpegPath) ? Audio.FfmpegLocator.Resolve() : ffmpegPath;
        if (!File.Exists(resolved) || !File.Exists(filePath))
        {
            return VideoMediaInfo.None;
        }

        CacheEntry? cached = TryGetCached(filePath);
        if (cached != null)
        {
            return cached.Info;
        }

        VideoMediaInfo info = ParseStreamInfo(RunDescribe(resolved, filePath));
        StoreCache(filePath, info);
        return info;
    }

    /// <summary>
    /// Pulls the first real video stream out of ffmpeg's stderr banner. Attached pictures (album
    /// art in an audio file) are ignored, because they are reported as video streams too.
    /// </summary>
    public static VideoMediaInfo ParseStreamInfo(string? ffmpegOutput)
    {
        if (string.IsNullOrWhiteSpace(ffmpegOutput))
        {
            return VideoMediaInfo.None;
        }

        foreach (string rawLine in ffmpegOutput.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (!line.Contains("Stream #", StringComparison.Ordinal) ||
                !line.Contains("Video:", StringComparison.Ordinal) ||
                line.Contains("(attached pic)", StringComparison.Ordinal))
            {
                continue;
            }

            Match size = FrameSizeRegex().Match(line);
            if (!size.Success ||
                !int.TryParse(size.Groups[1].Value, CultureInfo.InvariantCulture, out int width) ||
                !int.TryParse(size.Groups[2].Value, CultureInfo.InvariantCulture, out int height))
            {
                continue;
            }

            if (width <= 0 || height <= 0)
            {
                continue;
            }

            double frameRate = FallbackFrameRate;
            Match rate = FrameRateRegex().Match(line);
            if (rate.Success &&
                double.TryParse(rate.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) &&
                parsed > 0 && parsed <= 240)
            {
                frameRate = parsed;
            }

            return new VideoMediaInfo(true, width, height, frameRate);
        }

        return VideoMediaInfo.None;
    }

    /// <summary>Drops every cached probe result (used by tests and after a re-download).</summary>
    public static void ClearCache() => Cache.Clear();

    /// <summary>Forgets the cached result of a single file.</summary>
    public static void Invalidate(string filePath) => Cache.TryRemove(filePath, out _);

    private static string RunDescribe(string ffmpegPath, string filePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in new[] { "-hide_banner", "-nostdin", "-i", filePath })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return string.Empty;
            }

            string output = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(ProbeTimeoutMs))
            {
                TryKill(process);
            }

            return output;
        }
        catch (Exception)
        {
            TryKill(process);
            return string.Empty;
        }
    }

    private static CacheEntry? TryGetCached(string filePath)
    {
        if (!Cache.TryGetValue(filePath, out CacheEntry? entry))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(filePath);
            return entry.Length == info.Length && entry.LastWriteUtc == info.LastWriteTimeUtc ? entry : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void StoreCache(string filePath, VideoMediaInfo info)
    {
        try
        {
            var file = new FileInfo(filePath);
            Cache[filePath] = new CacheEntry(file.Length, file.LastWriteTimeUtc, info);
        }
        catch (Exception)
        {
            // Probing again next time is cheaper than failing playback over a cache write.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Already gone; nothing left to clean up.
        }
    }

    private sealed record CacheEntry(long Length, DateTime LastWriteUtc, VideoMediaInfo Info);
}
