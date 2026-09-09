using System.Text.Json;

namespace OpenKaraoke.Core.Configuration;

/// <summary>
/// Reads app configuration from safe, git-ignored sources only:
/// environment variables first, then an optional local JSON file next to the app.
/// Never ships secrets in source code.
/// </summary>
public static class AppSettings
{
    public const string YoutubeApiKeyEnvVar = "OPEN_KARAOKE_YOUTUBE_API_KEY";
    public const string LocalConfigFileName = "appsettings.local.json";

    public static string? GetYoutubeApiKey(string? configDirectory = null)
    {
        string? fromEnv = Environment.GetEnvironmentVariable(YoutubeApiKeyEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        string? fromFile = ReadLocalConfig(configDirectory ?? AppContext.BaseDirectory);
        return fromFile;
    }

    private static string? ReadLocalConfig(string directory)
    {
        string path = Path.Combine(directory, LocalConfigFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("Youtube", out JsonElement youtube) &&
                youtube.TryGetProperty("ApiKey", out JsonElement key) &&
                key.ValueKind == JsonValueKind.String)
            {
                string? value = key.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        return null;
    }
}
