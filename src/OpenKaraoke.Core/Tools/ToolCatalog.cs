using OpenKaraoke.Core.Audio;
using OpenKaraoke.Core.Download;
using OpenKaraoke.Core.Platform;

namespace OpenKaraoke.Core.Tools;

/// <summary>External command line tools the app can download for the operator.</summary>
public enum ToolKind
{
    /// <summary>yt-dlp — YouTube에서 MR(반주) 영상/음원을 내려받는 도구.</summary>
    YtDlp,

    /// <summary>ffmpeg — 내려받은 영상·음원을 병합하고 디코딩해 재생하는 도구.</summary>
    Ffmpeg,
}

/// <summary>
/// One external tool: where the app looks for it, whether it was found, and whether this
/// platform needs it at all. Drives the startup "설치할까요?" dialog.
/// </summary>
public sealed record ToolRequirement(
    ToolKind Kind,
    string DisplayName,
    string FileName,
    string Purpose,
    string DownloadSize,
    string ExpectedPath,
    bool IsInstalled,
    bool IsRequired);

/// <summary>Snapshot of every external tool the app depends on.</summary>
public static class ToolCatalog
{
    /// <summary>File name the app looks for next to itself (platform suffix included).</summary>
    public static string FileNameFor(ToolKind kind)
        => ExecutableLocator.WithPlatformSuffix(kind == ToolKind.YtDlp ? "yt-dlp" : "ffmpeg");

    /// <summary>Inspects both tools using the same resolution the app applies at runtime.</summary>
    public static IReadOnlyList<ToolRequirement> Inspect() => [InspectYtDlp(), InspectFfmpeg()];

    /// <summary>True when this platform is missing a tool it cannot work without.</summary>
    public static bool AnyRequiredMissing()
        => Inspect().Any(tool => tool.IsRequired && !tool.IsInstalled);

    /// <summary>
    /// Every platform needs ffmpeg now: it merges the downloaded video+audio into one mp4 and
    /// decodes the video frames that are shown on the customer monitor.
    /// </summary>
    private static bool IsFfmpegRequired => true;
    private static ToolRequirement InspectYtDlp()
    {
        string path = YtDlpProcessRunner.ResolveExecutable(null);
        return new ToolRequirement(
            ToolKind.YtDlp,
            "yt-dlp",
            FileNameFor(ToolKind.YtDlp),
            "YouTube에서 MR(반주) 영상을 내려받을 때 사용합니다.",
            "약 20MB",
            path,
            IsInstalled: File.Exists(path),
            IsRequired: true);
    }

    private static ToolRequirement InspectFfmpeg()
    {
        string path = FfmpegLocator.Resolve();
        return new ToolRequirement(
            ToolKind.Ffmpeg,
            "ffmpeg",
            FileNameFor(ToolKind.Ffmpeg),
            "내려받은 영상·음원을 병합하고 재생(가사 화면 표시)할 때 반드시 필요합니다.",
            "약 100MB",
            path,
            IsInstalled: File.Exists(path),
            IsRequired: IsFfmpegRequired);
    }
}
