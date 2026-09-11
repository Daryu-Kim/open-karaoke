namespace OpenKaraoke.Core.Download;

/// <summary>
/// Builds the yt-dlp command line for downloading a single video together with its audio.
/// Uses ArgumentList (no shell quoting pitfalls) and an id-based output template so the
/// resulting file can be located deterministically afterwards.
/// </summary>
public static class YtDlpArguments
{
    public const string WatchUrlFormat = "https://www.youtube.com/watch?v={0}";

    /// <summary>
    /// Preferred format list: video+audio up to 1080p, H.264 first because the karaoke PC decodes
    /// H.264 in software while AV1/VP9 1080p can consume an entire core. m4a (AAC) audio is natively
    /// decodable by the Windows audio stack, and both fall back to any stream when unavailable.
    /// </summary>
    public const string FormatSelection =
        "bv*[height<=1080][vcodec^=avc1]+ba[ext=m4a]" +
        "/bv*[height<=1080][ext=mp4]+ba[ext=m4a]" +
        "/b[height<=1080][ext=mp4]" +
        "/bv*[height<=1080]+ba" +
        "/b[height<=1080]" +
        "/b";

    /// <summary>Container used when yt-dlp has to mux a separate video and audio stream.</summary>
    public const string MergeOutputFormat = "mp4";

    public static string BuildOutputTemplate(string outputDirectory)
    {
        // %(id)s keeps the file name ASCII-only so it survives every filesystem.
        return Path.Combine(outputDirectory, "%(id)s.%(ext)s");
    }

    public static string BuildUrl(string videoId) => string.Format(WatchUrlFormat, videoId);

    public static IReadOnlyList<string> Build(string videoId, string outputDirectory, string? ffmpegPath = null)
    {
        var arguments = new List<string>
        {
            "--no-playlist",
            "--no-warnings",
            "--no-mtime",
            "--newline",
            "--socket-timeout", "30",
            "--retries", "3",
            "-f", FormatSelection,
            "--merge-output-format", MergeOutputFormat,
        };

        // Merging video+audio needs ffmpeg. yt-dlp searches the PATH and its own folder, which can
        // miss the copy this app installed next to itself (or found via the 설정 screen), so the
        // resolved binary is handed over explicitly.
        if (!string.IsNullOrWhiteSpace(ffmpegPath))
        {
            arguments.Add("--ffmpeg-location");
            arguments.Add(ffmpegPath);
        }

        arguments.Add("-o");
        arguments.Add(BuildOutputTemplate(outputDirectory));
        arguments.Add(BuildUrl(videoId));
        return arguments;
    }
}
