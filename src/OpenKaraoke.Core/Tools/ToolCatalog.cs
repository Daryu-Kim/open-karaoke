using OpenKaraoke.Core.Audio;
using OpenKaraoke.Core.Download;
using OpenKaraoke.Core.Platform;

namespace OpenKaraoke.Core.Tools;

/// <summary>External command line tools the app can download for the operator.</summary>
public enum ToolKind
{
    /// <summary>yt-dlp — YouTube에서 MR(반주) 음원을 내려받는 도구.</summary>
    YtDlp,

    /// <summary>ffmpeg — Linux/macOS 재생 경로에서 음원을 디코딩하는 도구.</summary>
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

    /// <summary>Windows plays through the built-in engine, so only Linux/macOS require ffmpeg.</summary>
    private static bool IsFfmpegRequired => !OperatingSystem.IsWindows();

    private static ToolRequirement InspectYtDlp()
    {
        string path = YtDlpProcessRunner.ResolveExecutable(null);
        return new ToolRequirement(
            ToolKind.YtDlp,
            "yt-dlp",
            FileNameFor(ToolKind.YtDlp),
            "YouTube에서 MR(반주) 음원을 내려받을 때 사용합니다.",
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
            IsFfmpegRequired
                ? "내려받은 음원을 디코딩해 재생할 때 반드시 필요합니다."
                : "받은 음원을 다른 형식으로 변환할 때 사용합니다. (Windows 재생 엔진만으로도 재생됩니다.)",
            "약 100MB",
            path,
            IsInstalled: File.Exists(path),
            IsRequired: IsFfmpegRequired);
    }
}
