using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenKaraoke.Core.Audio;
using OpenKaraoke.Core.Configuration;
using OpenKaraoke.Core.Download;
using OpenKaraoke.Desktop.Diagnostics;

namespace OpenKaraoke.Desktop.ViewModels;

/// <summary>
/// Backs the 설정 dialog: edits the YouTube Data API key and the ffmpeg / yt-dlp
/// executable paths and stores them in appsettings.local.json next to the app.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly string _dataDirectory;

    public SettingsViewModel(string songsDirectory)
    {
        _dataDirectory = Directory.GetParent(songsDirectory)?.FullName ?? songsDirectory;
        ApiKey = AppSettings.GetYoutubeApiKey() ?? string.Empty;
        FfmpegPath = AppSettings.GetFfmpegPath() ?? string.Empty;
        YtDlpPath = AppSettings.GetYtDlpPath() ?? string.Empty;
        EnvironmentWarning = BuildEnvironmentWarning();
        RefreshToolStatus();
    }

    [ObservableProperty]
    private string _apiKey;

    [ObservableProperty]
    private string _ffmpegPath;

    [ObservableProperty]
    private string _ytDlpPath;

    [ObservableProperty]
    private string _apiKeyStatus = string.Empty;

    [ObservableProperty]
    private string _ffmpegStatus = string.Empty;

    [ObservableProperty]
    private string _ytDlpStatus = string.Empty;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private bool _isSuccess;

    /// <summary>Notice shown when an environment variable wins over the stored value.</summary>
    public string? EnvironmentWarning { get; }

    public bool HasEnvironmentWarning => EnvironmentWarning is not null;

    /// <summary>Path of the JSON file this dialog writes.</summary>
    public string ConfigFilePath => AppSettings.GetLocalConfigPath();

    /// <summary>Folder holding the library database and the downloaded MR files.</summary>
    public string DataDirectory => _dataDirectory;

    /// <summary>Path of the diagnostics log file.</summary>
    public string LogFilePath => AppLog.LogFilePath;

    /// <summary>True once something was stored; the shell then re-applies the settings.</summary>
    public bool Saved { get; private set; }

    /// <summary>
    /// Validates and stores the edited values. Returns through <see cref="StatusText"/>
    /// instead of throwing so the dialog stays open on failure.
    /// </summary>
    public void Save()
    {
        string apiKey = ApiKey.Trim();
        string ffmpeg = FfmpegPath.Trim();
        string ytDlp = YtDlpPath.Trim();

        if (ffmpeg.Length > 0 && !File.Exists(ffmpeg))
        {
            SetStatus($"ffmpeg 실행 파일을 찾을 수 없습니다: {ffmpeg}", failed: true);
            return;
        }

        if (ytDlp.Length > 0 && !File.Exists(ytDlp))
        {
            SetStatus($"yt-dlp 실행 파일을 찾을 수 없습니다: {ytDlp}", failed: true);
            return;
        }

        try
        {
            AppSettings.Save(apiKey, ffmpeg, ytDlp);
        }
        catch (Exception ex)
        {
            AppLog.Write($"[settings] 저장 실패: {ex.GetType().Name}: {ex.Message}");
            SetStatus($"설정을 저장하지 못했습니다: {ex.Message}", failed: true);
            return;
        }

        Saved = true;
        RefreshToolStatus();
        AppLog.Write(
            $"[settings] 저장됨 (YouTube 키 {(apiKey.Length > 0 ? "설정" : "없음")}, "
            + $"ffmpeg {(ffmpeg.Length > 0 ? ffmpeg : "자동 탐색")}, "
            + $"yt-dlp {(ytDlp.Length > 0 ? ytDlp : "자동 탐색")})");
        SetStatus("설정을 저장했습니다. FFmpeg·yt-dlp는 다음 작업부터, API 키는 다음 검색부터 적용됩니다.", failed: false);
    }

    /// <summary>Re-reads the tool locations so the dialog shows what would actually run.</summary>
    public void RefreshToolStatus()
    {
        string ffmpeg = FfmpegLocator.Resolve();
        FfmpegStatus = File.Exists(ffmpeg)
            ? $"현재 사용: {ffmpeg}"
            : "ffmpeg를 찾지 못했습니다. 경로를 지정하거나 PATH에 설치해 주세요.";

        string ytDlp = YtDlpProcessRunner.ResolveExecutable(null);
        YtDlpStatus = File.Exists(ytDlp)
            ? $"현재 사용: {ytDlp}"
            : "yt-dlp를 찾지 못했습니다. 경로를 지정하거나 앱 폴더에 넣어 주세요.";

        ApiKeyStatus = string.IsNullOrWhiteSpace(AppSettings.GetYoutubeApiKey())
            ? "키가 없으면 YouTube 검색을 사용할 수 없습니다."
            : "키가 설정되어 있습니다.";
    }

    private void SetStatus(string text, bool failed)
    {
        StatusText = text;
        IsFailed = failed;
        IsSuccess = !failed;
    }

    private static string? BuildEnvironmentWarning()
    {
        string[] overridden =
        [
            AppSettings.YoutubeApiKeyEnvVar,
            AppSettings.FfmpegPathEnvVar,
            AppSettings.YtDlpPathEnvVar,
        ];

        string[] active = overridden
            .Where(variable => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            .ToArray();

        return active.Length == 0
            ? null
            : $"환경 변수({string.Join(", ", active)})가 설정되어 있어 여기서 저장한 값보다 우선 적용됩니다.";
    }
}
