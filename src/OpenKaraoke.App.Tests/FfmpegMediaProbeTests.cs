using OpenKaraoke.Core.Video;

namespace OpenKaraoke.App.Tests;

public class FfmpegMediaProbeTests
{
    private const string FfmpegBanner = """
        Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'C:\songs\abc123.mp4':
          Metadata:
            major_brand     : isom
          Duration: 00:04:21.03, start: 0.000000, bitrate: 2103 kb/s
        """;

    [Fact]
    public void ParseStreamInfo_Reads1080pVideoStream()
    {
        string output = FfmpegBanner + "\n" +
            "  Stream #0:0(und): Video: h264 (High) (avc1 / 0x31637661), yuv420p, 1920x1080 [SAR 1:1 DAR 16:9], " +
            "2000 kb/s, 30 fps, 30 tbr, 15360 tbn (default)\n" +
            "  Stream #0:1(und): Audio: aac (LC) (mp4a / 0x6134706D), 44100 Hz, stereo, fltp, 128 kb/s\n";

        VideoMediaInfo info = FfmpegMediaProbe.ParseStreamInfo(output);

        Assert.True(info.HasVideo);
        Assert.Equal(1920, info.Width);
        Assert.Equal(1080, info.Height);
        Assert.Equal(30d, info.FramesPerSecond);
    }

    [Theory]
    [InlineData("25 fps, 25 tbr, 12800 tbn", 25d)]
    [InlineData("29.97 fps, 29.97 tbr, 30000 tbn", 29.97d)]
    [InlineData("59.94 fps, 59.94 tbr, 60000 tbn", 59.94d)]
    public void ParseStreamInfo_ReadsFrameRate(string frameRateToken, double expected)
    {
        string line = "  Stream #0:0: Video: h264, yuv420p, 1280x720, 3000 kb/s, " + frameRateToken + "\n";

        VideoMediaInfo info = FfmpegMediaProbe.ParseStreamInfo(line);

        Assert.Equal(expected, info.FramesPerSecond);
    }

    [Fact]
    public void ParseStreamInfo_FallsBackToThirtyFps()
    {
        VideoMediaInfo info = FfmpegMediaProbe.ParseStreamInfo(
            "  Stream #0:0: Video: h264, yuv420p, 1280x720, 3000 kb/s, 90k tbr, 90k tbn\n");

        Assert.Equal(30d, info.FramesPerSecond);
    }

    [Fact]
    public void ParseStreamInfo_HandlesVerticalVideo()
    {
        VideoMediaInfo info = FfmpegMediaProbe.ParseStreamInfo(
            "  Stream #0:0: Video: h264, yuv420p, 1080x1920 [SAR 1:1 DAR 9:16], 30 fps, 30 tbr\n");

        Assert.True(info.HasVideo);
        Assert.Equal(1080, info.Width);
        Assert.Equal(1920, info.Height);
    }

    [Fact]
    public void ParseStreamInfo_SkipsAttachedPicture()
    {
        string output = """
            Input #0, mov,mp4,m4a, from 'C:\songs\old.m4a':
              Duration: 00:03:52.11, start: 0.000000, bitrate: 129 kb/s
              Stream #0:0(und): Audio: aac (LC) (mp4a / 0x6134706D), 44100 Hz, stereo, fltp, 128 kb/s
              Stream #0:1(und): Video: mjpeg (Baseline) (mjpeg / 0x6765706A), yuvj420p, 1280x720, 90k tbr, 90k tbn (attached pic)
            """;

        VideoMediaInfo info = FfmpegMediaProbe.ParseStreamInfo(output);

        Assert.False(info.HasVideo);
        Assert.Equal(VideoMediaInfo.None, info);
    }

    [Fact]
    public void ParseStreamInfo_IgnoresAttachedPictureAndKeepsRealVideo()
    {
        string output = """
              Stream #0:0(und): Video: h264 (High) (avc1 / 0x31637661), yuv420p, 1920x1080, 30 fps, 30 tbr (default)
              Stream #0:1(und): Video: mjpeg (Baseline) (mjpeg / 0x6765706A), yuvj420p, 640x480 (attached pic)
            """;

        VideoMediaInfo info = FfmpegMediaProbe.ParseStreamInfo(output);

        Assert.Equal(1920, info.Width);
        Assert.Equal(1080, info.Height);
    }

    [Fact]
    public void ParseStreamInfo_ReturnsNoneForAudioOnlyFile()
    {
        string output = """
            Input #0, mp3, from 'C:\songs\abc123.mp3':
              Duration: 00:03:12.00, start: 0.000000, bitrate: 320 kb/s
              Stream #0:0: Audio: mp3, 44100 Hz, stereo, fltp, 320 kb/s
            """;

        VideoMediaInfo info = FfmpegMediaProbe.ParseStreamInfo(output);

        Assert.False(info.HasVideo);
        Assert.False(info.IsUsable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseStreamInfo_ReturnsNoneForEmptyOutput(string? output)
    {
        Assert.Equal(VideoMediaInfo.None, FfmpegMediaProbe.ParseStreamInfo(output));
    }
}
