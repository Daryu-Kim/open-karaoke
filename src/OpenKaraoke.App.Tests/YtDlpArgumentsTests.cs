using OpenKaraoke.Core.Download;

namespace OpenKaraoke.App.Tests;

public class YtDlpArgumentsTests
{
    [Fact]
    public void Build_ContainsUrl_Format_AndTemplate()
    {
        IReadOnlyList<string> args = YtDlpArguments.Build("abc123XYZ", "C:\\data\\songs");

        Assert.Contains("https://www.youtube.com/watch?v=abc123XYZ", args);
        Assert.Contains(YtDlpArguments.FormatSelection, args);
        Assert.Contains("C:\\data\\songs\\%(id)s.%(ext)s", args);
        Assert.Contains("--no-playlist", args);
        Assert.Contains("--newline", args);
    }

    [Fact]
    public void Build_AsksForMp4Container()
    {
        IReadOnlyList<string> args = YtDlpArguments.Build("abc123XYZ", "C:\\data\\songs");

        int index = args.ToList().IndexOf("--merge-output-format");

        Assert.True(index >= 0);
        Assert.Equal("mp4", args[index + 1]);
        Assert.Equal("mp4", YtDlpArguments.MergeOutputFormat);
    }

    [Fact]
    public void FormatSelection_Prefers1080pH264()
    {
        Assert.Contains("height<=1080", YtDlpArguments.FormatSelection);
        Assert.StartsWith("bv*[height<=1080][vcodec^=avc1]", YtDlpArguments.FormatSelection);
    }

    [Fact]
    public void Build_PointsYtDlpAtTheFfmpegUsedForMerging()
    {
        IReadOnlyList<string> args = YtDlpArguments.Build("abc123XYZ", "C:\\data\\songs", "C:\\app\\ffmpeg.exe");

        int index = args.ToList().IndexOf("--ffmpeg-location");

        Assert.True(index >= 0);
        Assert.Equal("C:\\app\\ffmpeg.exe", args[index + 1]);
    }

    [Fact]
    public void Build_OmitsFfmpegLocationWhenNoBinaryWasFound()
    {
        IReadOnlyList<string> args = YtDlpArguments.Build("abc123XYZ", "C:\\data\\songs");

        Assert.DoesNotContain("--ffmpeg-location", args);
    }

    [Fact]
    public void BuildOutputTemplate_UsesIdAndExt()
    {
        Assert.Equal("C:\\lib\\%(id)s.%(ext)s", YtDlpArguments.BuildOutputTemplate("C:\\lib"));
    }

    [Fact]
    public void BuildUrl_FormatsWatchUrl()
    {
        Assert.Equal("https://www.youtube.com/watch?v=abc123", YtDlpArguments.BuildUrl("abc123"));
    }
}
