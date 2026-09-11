using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenKaraoke.Core.Download;

/// <summary>
/// Parses yt-dlp "newline" stdout so the app can show live progress and learn the final
/// file path without scanning the output directory. Pure logic, unit-testable.
/// </summary>
public sealed class YtDlpOutputTracker
{
    private static readonly Regex ProgressRegex =
        new(@"^\[download\]\s+([0-9]+(?:\.[0-9]+)?)%", RegexOptions.Compiled);

    private static readonly Regex DestinationRegex =
        new(@"^\[download\]\s+Destination:\s+(?<path>.+)$", RegexOptions.Compiled);

    private static readonly Regex AlreadyDownloadedRegex =
        new(@"^\[download\]\s+(?<path>.+?)\s+has already been downloaded$", RegexOptions.Compiled);

    private static readonly Regex MergeRegex =
        new(@"^\[(?:Merger|ffmpeg)\]\s+Merging formats into\s+""(?<path>.+)""$", RegexOptions.Compiled);

    /// <summary>
    /// Last file destination reported by yt-dlp, if any. When the video and audio streams are merged
    /// this is the merged file, which is the one the library has to point at.
    /// </summary>
    public string? DestinationPath { get; private set; }

    /// <summary>Most recently reported download percentage (0-100).</summary>
    public double? LastProgressPercent { get; private set; }

    /// <summary>True once a 100% progress line has been seen.</summary>
    public bool Completed { get; private set; }

    /// <summary>Raw ERROR: lines (yt-dlp prints user-facing errors prefixed with ERROR:).</summary>
    public List<string> Errors { get; } = new();

    public void ProcessLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        string trimmed = line.Trim();

        if (trimmed.StartsWith("ERROR:", StringComparison.Ordinal))
        {
            Errors.Add(trimmed.Substring("ERROR:".Length).Trim());
            return;
        }

        Match progress = ProgressRegex.Match(trimmed);
        if (progress.Success &&
            double.TryParse(progress.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double percent))
        {
            LastProgressPercent = percent;
            if (percent >= 100.0)
            {
                Completed = true;
            }

            return;
        }

        Match destination = DestinationRegex.Match(trimmed);
        if (destination.Success)
        {
            DestinationPath = destination.Groups["path"].Value.Trim();
            return;
        }

        Match already = AlreadyDownloadedRegex.Match(trimmed);
        if (already.Success)
        {
            DestinationPath = already.Groups["path"].Value.Trim();
            Completed = true;
            return;
        }

        // The separately downloaded video/audio parts are deleted once ffmpeg muxed them,
        // so the merged file (not the part reported by the last "Destination:" line) is the result.
        Match merge = MergeRegex.Match(trimmed);
        if (merge.Success)
        {
            DestinationPath = merge.Groups["path"].Value.Trim();
            Completed = true;
        }
    }
}
