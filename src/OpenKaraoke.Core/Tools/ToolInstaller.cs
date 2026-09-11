using System.Net.Http;
using System.Runtime.InteropServices;

namespace OpenKaraoke.Core.Tools;

/// <summary>Progress of one download/install (percent 0-100, or -1 when the length is unknown).</summary>
public sealed record ToolInstallProgress(double Percent, string Message)
{
    /// <summary>Progress whose total length the server did not report.</summary>
    public static ToolInstallProgress Unknown(string message) => new(-1, message);
}

/// <summary>Install failure carrying a Korean message that can be shown to the operator as is.</summary>
public sealed class ToolInstallException : Exception
{
    public ToolInstallException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Downloads the external tools the app needs into its own folder so a shop PC never has to be
/// prepared by hand: yt-dlp from the official yt-dlp release, ffmpeg from the BtbN FFmpeg
/// builds. Both land next to the executable — the first location the app searches — which means
/// no administrator rights are required on either platform.
/// </summary>
public sealed class ToolInstaller : IDisposable
{
    private const string YtDlpDownloadBase = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";
    private const string FfmpegDownloadBase = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _targetDirectory;
    private readonly IToolArchiveExtractor _extractor;

    public ToolInstaller(
        HttpClient? http = null,
        string? targetDirectory = null,
        IToolArchiveExtractor? extractor = null)
    {
        _ownsHttp = http is null;
        _http = http ?? CreateDefaultClient();
        _targetDirectory = targetDirectory ?? AppContext.BaseDirectory;
        _extractor = extractor ?? ToolArchiveExtractor.ForCurrentPlatform();
    }

    /// <summary>Folder the tools are installed into (next to the running app).</summary>
    public string TargetDirectory => _targetDirectory;

    /// <summary>Where <paramref name="kind"/> ends up after a successful install.</summary>
    public string TargetPathFor(ToolKind kind) => Path.Combine(_targetDirectory, ToolCatalog.FileNameFor(kind));

    /// <summary>
    /// Installs <paramref name="kind"/> and returns the path of the installed executable.
    /// Throws <see cref="ToolInstallException"/> with a Korean explanation on failure and
    /// leaves no half-written executable behind.
    /// </summary>
    public async Task<string> InstallAsync(
        ToolKind kind,
        IProgress<ToolInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string displayName = kind == ToolKind.YtDlp ? "yt-dlp" : "ffmpeg";
        string destination = TargetPathFor(kind);
        string staging = destination + ".part";
        string? archive = null;

        try
        {
            if (kind == ToolKind.YtDlp)
            {
                progress?.Report(new ToolInstallProgress(0, $"{displayName} 내려받는 중..."));
                await DownloadAsync(DownloadUrlFor(kind), staging, displayName, progress, cancellationToken);
            }
            else
            {
                archive = Path.Combine(Path.GetTempPath(), $"open-karaoke-ffmpeg-{Guid.NewGuid():N}{ArchiveSuffix}");
                progress?.Report(new ToolInstallProgress(0, "ffmpeg 압축 파일 내려받는 중..."));
                await DownloadAsync(DownloadUrlFor(kind), archive, displayName, progress, cancellationToken);
                progress?.Report(ToolInstallProgress.Unknown("압축을 푸는 중..."));
                await _extractor.ExtractAsync(archive, ToolCatalog.FileNameFor(kind), staging, cancellationToken);
            }

            File.Move(staging, destination, overwrite: true);
            MakeExecutable(destination);
            progress?.Report(new ToolInstallProgress(100, $"{displayName} 설치 완료"));
            return destination;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ToolInstallException)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ToolInstallException(
                $"앱 폴더에 파일을 쓸 수 없습니다: {_targetDirectory}\n"
                + "앱을 쓰기 가능한 폴더로 옮기거나 관리자 권한으로 실행한 뒤 다시 시도해 주세요.",
                ex);
        }
        catch (Exception ex)
        {
            throw new ToolInstallException($"{displayName} 설치에 실패했습니다: {ex.Message}", ex);
        }
        finally
        {
            TryDelete(staging);
            if (archive is not null)
            {
                TryDelete(archive);
            }
        }
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    /// <summary>Official download location of <paramref name="kind"/> for this platform.</summary>
    public static string DownloadUrlFor(ToolKind kind)
        => kind == ToolKind.YtDlp
            ? YtDlpDownloadBase + YtDlpAssetName()
            : FfmpegDownloadBase + FfmpegAssetName();

    private static string YtDlpAssetName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "yt-dlp.exe";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "yt-dlp_macos";
        }

        return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "yt-dlp_linux_aarch64" : "yt-dlp_linux";
    }

    private static string FfmpegAssetName()
    {
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "64",
            _ => throw new ToolInstallException(
                $"이 프로세서({RuntimeInformation.ProcessArchitecture})용 ffmpeg 자동 설치를 지원하지 않습니다. "
                + "LINUX.md의 안내에 따라 직접 설치해 주세요."),
        };

        if (OperatingSystem.IsWindows())
        {
            return $"ffmpeg-master-latest-win{architecture}-gpl.zip";
        }

        if (OperatingSystem.IsLinux())
        {
            return $"ffmpeg-master-latest-linux{architecture}-gpl.tar.xz";
        }

        throw new ToolInstallException(
            "이 운영 체제에서는 ffmpeg 자동 설치를 지원하지 않습니다. "
            + "macOS는 'brew install ffmpeg'로 설치한 뒤 다시 시도해 주세요.");
    }

    /// <summary>Extension of the ffmpeg archive published for this platform.</summary>
    private static string ArchiveSuffix => OperatingSystem.IsWindows() ? ".zip" : ".tar.xz";

    private async Task DownloadAsync(
        string url,
        string destinationPath,
        string displayName,
        IProgress<ToolInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new ToolInstallException(
                $"{displayName} 내려받기에 실패했습니다 (HTTP {(int)response.StatusCode}). "
                + "인터넷 연결을 확인한 뒤 다시 시도해 주세요.");
        }

        long? total = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream target = File.Create(destinationPath);

        byte[] buffer = new byte[128 * 1024];
        long received = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            progress?.Report(Describe(displayName, received, total));
        }

        if (total is > 0 && received < total.Value)
        {
            throw new ToolInstallException($"{displayName} 내려받기가 중간에 끊겼습니다. 다시 시도해 주세요.");
        }
    }

    private static ToolInstallProgress Describe(string displayName, long received, long? total)
    {
        const double Megabyte = 1024 * 1024;
        double receivedMb = received / Megabyte;

        return total is > 0
            ? new ToolInstallProgress(
                receivedMb / (total.Value / Megabyte) * 100,
                $"{displayName} 내려받는 중... {receivedMb:F0} / {total.Value / Megabyte}MB")
            : ToolInstallProgress.Unknown($"{displayName} 내려받는 중... {receivedMb:F0}MB");
    }

    private static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // Temporary file only; a failure here must not hide the install result.
        }
    }

    private static HttpClient CreateDefaultClient()
    {
        // Release downloads are ~100MB, so the default 100s timeout would be too tight.
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("open-karaoke/1.0");
        return client;
    }
}
