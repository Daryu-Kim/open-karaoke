using OpenKaraoke.Core.Download;

namespace OpenKaraoke.App.Tests;

public class YtDlpOutputTrackerTests
{
    [Fact]
    public void ProgressLines_UpdatePercent()
    {
        var tracker = new YtDlpOutputTracker();
        tracker.ProcessLine("[download]  12.5% of  3.20MiB at  1.02MiB/s ETA 00:00");
        Assert.Equal(12.5, tracker.LastProgressPercent);
        Assert.False(tracker.Completed);
        Assert.Null(tracker.DestinationPath);
    }

    [Fact]
    public void HundredPercent_MarksCompleted()
    {
        var tracker = new YtDlpOutputTracker();
        tracker.ProcessLine("[download] 100% of 3.20MiB in 00:00:03");
        Assert.True(tracker.Completed);
        Assert.Equal(100.0, tracker.LastProgressPercent);
    }

    [Fact]
    public void DestinationLine_CapturesPath()
    {
        var tracker = new YtDlpOutputTracker();
        tracker.ProcessLine("[download] Destination: C:\\karaoke\\library\\abc123.m4a");
        Assert.Equal("C:\\karaoke\\library\\abc123.m4a", tracker.DestinationPath);
    }

    [Fact]
    public void ErrorLine_IsCaptured()
    {
        var tracker = new YtDlpOutputTracker();
        tracker.ProcessLine("ERROR: [youtube] xyz: Video unavailable");
        tracker.ProcessLine("[youtube] xyz: Downloading webpage");
        Assert.Single(tracker.Errors);
        Assert.Equal("[youtube] xyz: Video unavailable", tracker.Errors[0]);
    }

    [Fact]
    public void AlreadyDownloadedLine_MarksCompletedAndSetsPath()
    {
        var tracker = new YtDlpOutputTracker();
        tracker.ProcessLine("[download] C:\\karaoke\\library\\abc123.m4a has already been downloaded");
        Assert.True(tracker.Completed);
        Assert.Equal("C:\\karaoke\\library\\abc123.m4a", tracker.DestinationPath);
    }

    [Fact]
    public void MergeLine_ReplacesPartPathWithMergedFile()
    {
        var tracker = new YtDlpOutputTracker();
        tracker.ProcessLine("[download] Destination: C:\\karaoke\\library\\abc123.f137.mp4");
        tracker.ProcessLine("[download] 100% of  3.20MiB in 00:00:03");
        tracker.ProcessLine("[download] Destination: C:\\karaoke\\library\\abc123.f140.m4a");
        tracker.ProcessLine("[download] 100% of  1.20MiB in 00:00:01");
        tracker.ProcessLine("[Merger] Merging formats into \"C:\\karaoke\\library\\abc123.mp4\"");
        tracker.ProcessLine("Deleting original file C:\\karaoke\\library\\abc123.f137.mp4 (pass -k to keep)");

        Assert.True(tracker.Completed);
        Assert.Equal("C:\\karaoke\\library\\abc123.mp4", tracker.DestinationPath);
    }

    [Fact]
    public void LegacyFfmpegMergeLine_ReplacesPartPathWithMergedFile()
    {
        var tracker = new YtDlpOutputTracker();
        tracker.ProcessLine("[ffmpeg] Merging formats into \"C:\\karaoke\\library\\abc123.mp4\"");

        Assert.True(tracker.Completed);
        Assert.Equal("C:\\karaoke\\library\\abc123.mp4", tracker.DestinationPath);
    }

    [Fact]
    public void NonDownloadLines_AreIgnored()
    {
        var tracker = new YtDlpOutputTracker();
        tracker.ProcessLine("[youtube] abc123: Downloading webpage");
        tracker.ProcessLine("");
        Assert.Null(tracker.DestinationPath);
        Assert.Null(tracker.LastProgressPercent);
        Assert.Empty(tracker.Errors);
    }

    [Fact]
    public void InformationalDownloadLines_DoNotCountAsProgress()
    {
        var tracker = new YtDlpOutputTracker();
        tracker.ProcessLine("[download] Downloading item 1 of 1");
        Assert.Null(tracker.LastProgressPercent);
    }
}
