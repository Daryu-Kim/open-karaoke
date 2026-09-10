namespace OpenKaraoke.Core.Platform;

/// <summary>
/// Cross-platform helper that locates external command line tools (yt-dlp, ffmpeg) so the
/// same code works on Windows (<c>tool.exe</c> next to the app) and Linux (<c>tool</c> on PATH).
/// </summary>
public static class ExecutableLocator
{
    /// <summary>Appends the platform executable suffix (<c>.exe</c> on Windows) to a base name.</summary>
    public static string WithPlatformSuffix(string baseName)
        => OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;

    /// <summary>
    /// Resolves a tool path by trying, in order: the explicit path, a copy shipped next to the
    /// application, the PATH, then any extra directories (used for common Linux install paths).
    /// Returns the first candidate that exists, or the "next to the app" path so callers can
    /// report a meaningful expected location.
    /// </summary>
    public static string Resolve(string? explicitPath, string baseName, params string[] extraDirectories)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        string fileName = WithPlatformSuffix(baseName);
        string nextToApp = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(nextToApp))
        {
            return nextToApp;
        }

        string? fromPath = FindOnPath(fileName);
        if (fromPath is not null)
        {
            return fromPath;
        }

        foreach (string directory in extraDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            try
            {
                string candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
                // Malformed candidate path; keep scanning.
            }
        }

        return nextToApp;
    }

    /// <summary>Searches every PATH entry for <paramref name="fileName"/>.</summary>
    public static string? FindOnPath(string fileName)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            return null;
        }

        foreach (string dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
                // Unreadable PATH entry; keep scanning.
            }
        }

        return null;
    }
}
