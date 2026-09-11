namespace OpenKaraoke.Core.Video;

/// <summary>
/// Video stream characteristics of one media file, as reported by ffmpeg.
/// <see cref="FramesPerSecond"/> is the constant output rate the frame reader is forced to,
/// so frame N corresponds to <c>start + N / FramesPerSecond</c> on the song timeline.
/// </summary>
public sealed record VideoMediaInfo(bool HasVideo, int Width, int Height, double FramesPerSecond)
{
    /// <summary>Result for files without a usable video stream (audio-only downloads).</summary>
    public static readonly VideoMediaInfo None = new(false, 0, 0, 0);

    /// <summary>True when the file carries a video stream we can decode and show.</summary>
    public bool IsUsable => HasVideo && Width > 0 && Height > 0 && FramesPerSecond > 0;
}
