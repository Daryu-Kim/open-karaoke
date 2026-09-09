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
