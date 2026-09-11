using OpenKaraoke.Core.Video;

namespace OpenKaraoke.App.Tests;

public class VideoGeometryTests
{
    [Fact]
    public void FitToHeight_Keeps1080pAsIs()
    {
        Assert.Equal((1920, 1080), VideoGeometry.FitToHeight(1920, 1080, 1080));
    }

    [Fact]
    public void FitToHeight_DownsizesFourK()
    {
        Assert.Equal((1920, 1080), VideoGeometry.FitToHeight(3840, 2160, 1080));
    }

    [Fact]
    public void FitToHeight_NeverUpscales()
    {
        Assert.Equal((1280, 720), VideoGeometry.FitToHeight(1280, 720, 1080));
    }

    [Fact]
    public void FitToHeight_RoundsBothEdgesToEvenNumbers()
    {
        Assert.Equal((1918, 1080), VideoGeometry.FitToHeight(1921, 1081, 1080));
    }

    [Fact]
    public void FitToHeight_CapsVerticalVideoHeightToo()
    {
        // A portrait 1080x1920 clip is still limited to 1080 pixels of height (608x1080).
        Assert.Equal((608, 1080), VideoGeometry.FitToHeight(1080, 1920, 1080));
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-1920, 1080)]
    public void FitToHeight_ReturnsZeroForInvalidSource(int width, int height)
    {
        Assert.Equal((0, 0), VideoGeometry.FitToHeight(width, height, 1080));
    }
}
