namespace OpenKaraoke.Core.Download;

/// <summary>
/// Builds the yt-dlp command line for an audio-only download of a single video.
/// Uses ArgumentList (no shell quoting pitfalls) and an id-based output template so the
/// resulting file can be located deterministically afterwards.
/// </summary>
public static class YtDlpArguments
{
    public const string WatchUrlFormat = "https://www.youtube.com/watch?v={0}";

    /// <summary>Preferred audio format list; m4a (AAC) is natively decodable by the Windows audio stack.</summary>
    public const string FormatSelection = "bestaudio[ext=m4a]/bestaudio[ext=mp3]/bestaudio";

    public static string BuildOutputTemplate(string outputDirectory)
    {
        // %(id)s keeps the file name ASCII-only so it survives every filesystem.
        return Path.Combine(outputDirectory, "%(id)s.%(ext)s");
    }

    public static string BuildUrl(string videoId) => string.Format(WatchUrlFormat, videoId);

    public static IReadOnlyList<string> Build(string videoId, string outputDirectory)
    {
        return new[]
        {
            "--no-playlist",
            "--no-warnings",
            "--no-mtime",
            "--newline",
            "--socket-timeout", "30",
            "--retries", "3",
            "-f", FormatSelection,
            "-o", BuildOutputTemplate(outputDirectory),
            BuildUrl(videoId),
        };
    }
}
