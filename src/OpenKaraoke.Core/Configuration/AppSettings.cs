using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenKaraoke.Core.Configuration;

/// <summary>
/// Reads and writes app configuration from safe, git-ignored sources only:
/// environment variables first, then an optional local JSON file next to the app.
/// Never ships secrets in source code.
/// </summary>
public static class AppSettings
{
    public const string YoutubeApiKeyEnvVar = "OPEN_KARAOKE_YOUTUBE_API_KEY";
    public const string FfmpegPathEnvVar = "OPENKARAOKE_FFMPEG";
    public const string YtDlpPathEnvVar = "OPENKARAOKE_YTDLP";
    public const string LocalConfigFileName = "appsettings.local.json";

    /// <summary>Section holding UI preferences that are not tool paths.</summary>
    public const string UiSection = "Ui";

    /// <summary>Set when the operator asked not to be reminded about missing tools again.</summary>
    public const string SkipToolInstallPromptKey = "SkipToolInstallPrompt";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Absolute path of the local JSON file that the 설정 screen writes.</summary>
    public static string GetLocalConfigPath(string? configDirectory = null)
        => Path.Combine(configDirectory ?? AppContext.BaseDirectory, LocalConfigFileName);

    /// <summary>YouTube Data API v3 key (환경 변수 → 설정 파일).</summary>
    public static string? GetYoutubeApiKey(string? configDirectory = null)
        => FromEnvironment(YoutubeApiKeyEnvVar)
            ?? ReadValue(GetLocalConfigPath(configDirectory), "Youtube", "ApiKey");

    /// <summary>Configured ffmpeg executable (환경 변수 → 설정 파일); null means auto-detect.</summary>
    public static string? GetFfmpegPath(string? configDirectory = null)
        => FromEnvironment(FfmpegPathEnvVar)
            ?? ReadValue(GetLocalConfigPath(configDirectory), "Tools", "FfmpegPath");

    /// <summary>Configured yt-dlp executable (환경 변수 → 설정 파일); null means auto-detect.</summary>
    public static string? GetYtDlpPath(string? configDirectory = null)
        => FromEnvironment(YtDlpPathEnvVar)
            ?? ReadValue(GetLocalConfigPath(configDirectory), "Tools", "YtDlpPath");

    /// <summary>
    /// Stores the values the 설정 screen exposes, keeping every other entry that already
    /// exists in the local file. Blank values are removed so auto-detection applies again.
    /// </summary>
    public static void Save(
        string? youtubeApiKey,
        string? ffmpegPath,
        string? ytDlpPath,
        string? configDirectory = null)
    {
        string path = GetLocalConfigPath(configDirectory);
        JsonObject root = ReadRoot(path);
        SetValue(root, "Youtube", "ApiKey", youtubeApiKey);
        SetValue(root, "Tools", "FfmpegPath", ffmpegPath);
        SetValue(root, "Tools", "YtDlpPath", ytDlpPath);

        File.WriteAllText(path, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
    }

    /// <summary>Reads a boolean flag from the local file; a missing or unparsable value is false.</summary>
    public static bool GetFlag(string section, string name, string? configDirectory = null)
        => ReadValue(GetLocalConfigPath(configDirectory), section, name) is { } value
            && bool.TryParse(value, out bool parsed)
            && parsed;

    /// <summary>Stores a boolean flag, removing the entry again when it is turned off.</summary>
    public static void SetFlag(string section, string name, bool value, string? configDirectory = null)
    {
        string path = GetLocalConfigPath(configDirectory);
        JsonObject root = ReadRoot(path);
        SetValue(root, section, name, value ? "true" : null);

        File.WriteAllText(path, root.ToJsonString(WriteOptions), new UTF8Encoding(false));
    }

    private static string? FromEnvironment(string variable)
    {
        string? value = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ReadValue(string path, string section, string name)
    {
        if (ReadRoot(path)[section] is not JsonObject group || group[name] is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
    }

    private static JsonObject ReadRoot(string path)
    {
        try
        {
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject root)
            {
                return root;
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new JsonObject();
    }

    private static void SetValue(JsonObject root, string section, string name, string? value)
    {
        if (root[section] is not JsonObject group)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            group = new JsonObject();
            root[section] = group;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            group.Remove(name);
            if (group.Count == 0)
            {
                root.Remove(section);
            }
        }
        else
        {
            group[name] = value.Trim();
        }
    }
}
