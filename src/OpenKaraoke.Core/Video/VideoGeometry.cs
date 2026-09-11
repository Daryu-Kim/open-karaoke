namespace OpenKaraoke.Core.Video;

/// <summary>Frame size math shared by the ffmpeg video pipeline.</summary>
public static class VideoGeometry
{
    /// <summary>
    /// Scales <paramref name="sourceWidth"/>×<paramref name="sourceHeight"/> down until the height
    /// fits <paramref name="targetHeight"/> (never up), keeping the aspect ratio and rounding both
    /// edges to even numbers because the rawvideo writer and yuv420p sources need even dimensions.
    /// </summary>
    public static (int Width, int Height) FitToHeight(int sourceWidth, int sourceHeight, int targetHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return (0, 0);
        }

        int height = targetHeight > 0 && sourceHeight > targetHeight ? targetHeight : sourceHeight;
        int width = (int)Math.Round(sourceWidth * (double)height / sourceHeight, MidpointRounding.AwayFromZero);
        return (MakeEven(width), MakeEven(height));
    }

    private static int MakeEven(int value) => Math.Max(2, value - (value % 2));
}
