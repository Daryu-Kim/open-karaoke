using System.Text.Json;
using OpenKaraoke.Core.Audio;
using OpenKaraoke.Core.Configuration;
using OpenKaraoke.Core.Download;

namespace OpenKaraoke.App.Tests;

/// <summary>
/// Covers the read/write behaviour behind the 설정 dialog: 저장 → 재실행 없이 반영, and the
/// environment-variable precedence that must never be broken by the dialog.
/// </summary>
public class AppSettingsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "oktest_" + Guid.NewGuid().ToString("N"));

    private readonly Dictionary<string, string?> _originalEnv = new();

    public AppSettingsTests()
    {
        Directory.CreateDirectory(_tempDir);

        foreach (string variable in Variables)
        {
            _originalEnv[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    public void Dispose()
    {
        foreach (KeyValuePair<string, string?> entry in _originalEnv)
        {
            Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        }

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static string[] Variables =>
    [
        AppSettings.YoutubeApiKeyEnvVar,
        AppSettings.FfmpegPathEnvVar,
        AppSettings.YtDlpPathEnvVar,
    ];

    private string ConfigPath => AppSettings.GetLocalConfigPath(_tempDir);

    private string CreateToolFile(string name)
    {
        string path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    [Fact]
    public void Save_ThenGetters_RoundTripEverySetting()
    {
        string ffmpeg = CreateToolFile("ffmpeg.exe");
        string ytDlp = CreateToolFile("yt-dlp.exe");

        AppSettings.Save("API-KEY-123", ffmpeg, ytDlp, _tempDir);

        Assert.True(File.Exists(ConfigPath));
        Assert.Equal("API-KEY-123", AppSettings.GetYoutubeApiKey(_tempDir));
        Assert.Equal(ffmpeg, AppSettings.GetFfmpegPath(_tempDir));
        Assert.Equal(ytDlp, AppSettings.GetYtDlpPath(_tempDir));
    }

    [Fact]
    public void Save_KeepsUnrelatedEntriesOfTheConfigFile()
    {
        File.WriteAllText(
            ConfigPath,
            """
            {
              "Youtube": { "ApiKey": "old-key", "Quota": 12 },
              "Custom": { "Keep": true }
            }
            """);

        AppSettings.Save("new-key", null, null, _tempDir);

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        Assert.Equal("new-key", doc.RootElement.GetProperty("Youtube").GetProperty("ApiKey").GetString());
        Assert.Equal(12, doc.RootElement.GetProperty("Youtube").GetProperty("Quota").GetInt32());
        Assert.True(doc.RootElement.GetProperty("Custom").GetProperty("Keep").GetBoolean());
    }

    [Fact]
    public void Save_BlankValues_RemoveEntriesAndRestoreAutoDetection()
    {
        AppSettings.Save("key", CreateToolFile("ffmpeg.exe"), CreateToolFile("yt-dlp.exe"), _tempDir);

        AppSettings.Save("   ", string.Empty, null, _tempDir);

        Assert.Null(AppSettings.GetYoutubeApiKey(_tempDir));
        Assert.Null(AppSettings.GetFfmpegPath(_tempDir));
        Assert.Null(AppSettings.GetYtDlpPath(_tempDir));

        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
        Assert.False(doc.RootElement.TryGetProperty("Youtube", out _));
        Assert.False(doc.RootElement.TryGetProperty("Tools", out _));
    }

    [Fact]
    public void Save_OverwritesMalformedFileWithValidJson()
    {
        File.WriteAllText(ConfigPath, "{ this is not json");

        AppSettings.Save("key", null, null, _tempDir);

        Assert.Equal("key", AppSettings.GetYoutubeApiKey(_tempDir));
    }

    [Fact]
    public void EnvironmentVariables_WinOverTheSavedFile()
    {
        string ffmpeg = CreateToolFile("env-ffmpeg.exe");
        string ytDlp = CreateToolFile("env-yt-dlp.exe");
        AppSettings.Save("file-key", null, null, _tempDir);

        Environment.SetEnvironmentVariable(AppSettings.YoutubeApiKeyEnvVar, "env-key");
        Environment.SetEnvironmentVariable(AppSettings.FfmpegPathEnvVar, ffmpeg);
        Environment.SetEnvironmentVariable(AppSettings.YtDlpPathEnvVar, ytDlp);

        Assert.Equal("env-key", AppSettings.GetYoutubeApiKey(_tempDir));
        Assert.Equal(ffmpeg, AppSettings.GetFfmpegPath(_tempDir));
        Assert.Equal(ytDlp, AppSettings.GetYtDlpPath(_tempDir));
    }

    [Fact]
    public void EnvironmentVariables_AreIgnoredWhenBlank()
    {
        AppSettings.Save("file-key", null, null, _tempDir);
        Environment.SetEnvironmentVariable(AppSettings.YoutubeApiKeyEnvVar, "  ");

        Assert.Equal("file-key", AppSettings.GetYoutubeApiKey(_tempDir));
    }

    [Fact]
    public void YtDlpRunner_HonoursTheConfiguredPath()
    {
        string ytDlp = CreateToolFile("yt-dlp-custom.exe");
        Environment.SetEnvironmentVariable(AppSettings.YtDlpPathEnvVar, ytDlp);

        Assert.Equal(ytDlp, YtDlpProcessRunner.ResolveExecutable(null));
        Assert.True(new YtDlpProcessRunner().IsAvailable);

        // An explicit path always wins over the configured one.
        string explicitTool = CreateToolFile("yt-dlp-explicit.exe");
        Assert.Equal(explicitTool, YtDlpProcessRunner.ResolveExecutable(explicitTool));
    }

    [Fact]
    public void FfmpegLocator_IgnoresConfiguredPathWhenMissing()
    {
        Environment.SetEnvironmentVariable(
            AppSettings.FfmpegPathEnvVar,
            Path.Combine(_tempDir, "does-not-exist-ffmpeg.exe"));

        string resolved = FfmpegLocator.Resolve();

        Assert.NotEqual(Path.Combine(_tempDir, "does-not-exist-ffmpeg.exe"), resolved);
    }
}
