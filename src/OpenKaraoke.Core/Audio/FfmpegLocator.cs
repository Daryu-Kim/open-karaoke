using OpenKaraoke.Core.Configuration;

namespace OpenKaraoke.Core.Audio;

/// <summary>
/// Locates the ffmpeg binary used by the Linux/OpenAL playback path to decode any source
/// audio (m4a, mp3, webm/opus) into raw float PCM.
/// </summary>
public static class FfmpegLocator
{
    private static readonly string[] LinuxSearchPaths =
    [
        "/usr/bin",
        "/usr/local/bin",
        "/bin",
        "/snap/bin",
        "/var/lib/flatpak/exports/bin",
        "/home/linuxbrew/.linuxbrew/bin",
    ];

    /// <summary>Environment variable that overrides the configured ffmpeg location.</summary>
    public const string OverrideVariable = AppSettings.FfmpegPathEnvVar;

    /// <summary>
    /// Resolves ffmpeg by checking the path configured in the 설정 screen (or the
    /// <see cref="OverrideVariable"/> environment variable), a binary next to the application,
    /// the PATH, then the usual Linux install directories.
    /// </summary>
    public static string Resolve()
        => Core.Platform.ExecutableLocator.Resolve(
            AppSettings.GetFfmpegPath(),
            "ffmpeg",
            LinuxSearchPaths);

    /// <summary>True when a usable ffmpeg binary can be found.</summary>
    public static bool IsAvailable() => File.Exists(Resolve());
}
