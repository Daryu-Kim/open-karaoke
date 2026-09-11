namespace OpenKaraoke.Core.Video;

/// <summary>
/// File-extension heuristics for "does this library file carry a picture?".
/// yt-dlp stores a merged video+audio download as mp4, while songs downloaded before
/// 가사 화면 support are audio-only (m4a/mp3/...), which is what the 영상 다시 받기 button keys on.
/// </summary>
public static class VideoFiles
{
    private static readonly string[] ContainerExtensions =
        [".mp4", ".m4v", ".mkv", ".webm", ".mov", ".avi", ".flv", ".wmv", ".mpg", ".mpeg", ".ts"];

    /// <summary>True when the path ends in a container that can hold a video stream.</summary>
    public static bool IsVideoContainer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return ContainerExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }
}
