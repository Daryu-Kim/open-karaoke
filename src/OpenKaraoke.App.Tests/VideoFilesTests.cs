using OpenKaraoke.Core.Video;

namespace OpenKaraoke.App.Tests;

public class VideoFilesTests
{
    [Theory]
    [InlineData("C:\\songs\\abc123.mp4")]
    [InlineData("C:\\songs\\abc123.MP4")]
    [InlineData("/home/karaoke/songs/abc123.webm")]
    [InlineData("C:\\songs\\abc123.mkv")]
    public void IsVideoContainer_AcceptsVideoContainers(string path)
    {
        Assert.True(VideoFiles.IsVideoContainer(path));
    }

    [Theory]
    [InlineData("C:\\songs\\abc123.m4a")]
    [InlineData("C:\\songs\\abc123.mp3")]
    [InlineData("C:\\songs\\abc123")]
    [InlineData("")]
    [InlineData(null)]
    public void IsVideoContainer_RejectsAudioOnlyFiles(string? path)
    {
        Assert.False(VideoFiles.IsVideoContainer(path));
    }
}
