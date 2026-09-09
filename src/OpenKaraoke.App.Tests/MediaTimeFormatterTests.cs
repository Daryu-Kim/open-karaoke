using OpenKaraoke.Core.Formatting;

namespace OpenKaraoke.App.Tests;

public class MediaTimeFormatterTests
{
    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(5, "00:05")]
    [InlineData(61, "01:01")]
    [InlineData(125, "02:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3661, "1:01:01")]
    [InlineData(7325, "2:02:05")]
    public void Format_TimeSpan_ProducesKaraokeClockStyle(int seconds, string expected)
    {
        var time = TimeSpan.FromSeconds(seconds);
        Assert.Equal(expected, MediaTimeFormatter.Format(time));
    }

    [Fact]
    public void Format_NegativeTimeSpan_ClampsToZero()
    {
        Assert.Equal("00:00", MediaTimeFormatter.Format(TimeSpan.FromSeconds(-3)));
    }

    [Theory]
    [InlineData(0.0, "00:00")]
    [InlineData(90.7, "01:30")]
    public void Format_SecondsDouble_ProducesSameStyle(double seconds, string expected)
    {
        Assert.Equal(expected, MediaTimeFormatter.Format(seconds));
    }
}
