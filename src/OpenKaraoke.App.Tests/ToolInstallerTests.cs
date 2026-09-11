using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using OpenKaraoke.Core.Configuration;
using OpenKaraoke.Core.Tools;

namespace OpenKaraoke.App.Tests;

/// <summary>
/// Covers the in-app tool download (yt-dlp / ffmpeg) behind the startup "설치할까요?" dialog:
/// where the binary lands, what the operator sees when it fails, and that a failed download
/// never leaves a half-written executable in the app folder.
/// </summary>
public class ToolInstallerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "oktools_" + Guid.NewGuid().ToString("N"));

    public ToolInstallerTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task YtDlp_downloads_official_build_into_app_folder()
    {
        byte[] payload = Encoding.UTF8.GetBytes("fake-yt-dlp-binary");
        var handler = new StubHandler(_ => Bytes(payload));
        using var http = new HttpClient(handler);
        using var installer = new ToolInstaller(http, _tempDir);
        var progress = new ListProgress();

        string installed = await installer.InstallAsync(ToolKind.YtDlp, progress);

        Assert.Equal(installer.TargetPathFor(ToolKind.YtDlp), installed);
        Assert.Equal(Path.Combine(_tempDir, ToolCatalog.FileNameFor(ToolKind.YtDlp)), installed);
        Assert.Equal(payload, File.ReadAllBytes(installed));
        Assert.Equal(ExpectedYtDlpUrl, handler.Requests.Single().ToString());

        // The whole point of the progress bar: 0% start, live download, 100% finish.
        Assert.Equal(0, progress.Values[0].Percent);
        Assert.Equal(100, progress.Values[^1].Percent);
        Assert.Contains("내려받는 중", progress.Values[1].Message);
        Assert.Contains("설치 완료", progress.Values[^1].Message);

        Assert.False(File.Exists(installed + ".part"));
    }

    [Fact]
    public async Task Http_failure_reports_korean_message_and_cleans_up()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        using var installer = new ToolInstaller(http, _tempDir);

        ToolInstallException error = await Assert.ThrowsAsync<ToolInstallException>(
            () => installer.InstallAsync(ToolKind.YtDlp));

        Assert.Contains("HTTP 404", error.Message);
        Assert.Contains("다시 시도", error.Message);
        Assert.Empty(Directory.GetFiles(_tempDir));
    }

    [Fact]
    public async Task Truncated_download_is_rejected()
    {
        byte[] payload = Encoding.UTF8.GetBytes("half-a-file");
        var handler = new StubHandler(_ =>
        {
            var content = new ByteArrayContent(payload);
            content.Headers.ContentLength = payload.Length * 10;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var http = new HttpClient(handler);
        using var installer = new ToolInstaller(http, _tempDir);

        ToolInstallException error = await Assert.ThrowsAsync<ToolInstallException>(
            () => installer.InstallAsync(ToolKind.YtDlp));

        Assert.Contains("끊겼습니다", error.Message);
        Assert.Empty(Directory.GetFiles(_tempDir));
    }

    [Fact]
    public async Task Install_failure_in_a_missing_folder_is_explained_in_korean()
    {
        var handler = new StubHandler(_ => Bytes(Encoding.UTF8.GetBytes("binary")));
        using var http = new HttpClient(handler);

        // A target folder that does not exist: the staging file cannot be created.
        string blocked = Path.Combine(_tempDir, "missing", "nested");
        using var installer = new ToolInstaller(http, blocked);

        ToolInstallException error = await Assert.ThrowsAsync<ToolInstallException>(
            () => installer.InstallAsync(ToolKind.YtDlp));

        Assert.Contains("yt-dlp 설치에 실패했습니다", error.Message);
    }

    [Fact]
    public async Task Ffmpeg_archive_is_extracted_and_ships_the_expected_member()
    {
        byte[] archive = Encoding.UTF8.GetBytes("archive-bytes");
        var handler = new StubHandler(_ => Bytes(archive));
        var extractor = new StubExtractor();
        using var http = new HttpClient(handler);
        using var installer = new ToolInstaller(http, _tempDir, extractor);

        string installed = await installer.InstallAsync(ToolKind.Ffmpeg);

        Assert.Equal(Path.Combine(_tempDir, ToolCatalog.FileNameFor(ToolKind.Ffmpeg)), installed);
        Assert.Equal(ToolCatalog.FileNameFor(ToolKind.Ffmpeg), extractor.MemberName);
        Assert.Equal(archive, extractor.ArchiveBytes);
        Assert.Equal("extracted-ffmpeg", File.ReadAllText(installed));

        // The ~100MB archive is a temporary file; nothing but the executable may stay behind.
        Assert.False(File.Exists(extractor.ArchivePath));
        Assert.Equal(new[] { installed }, Directory.GetFiles(_tempDir));
    }

    [Fact]
    public void Download_urls_point_at_the_official_releases()
    {
        Assert.Equal(ExpectedYtDlpUrl, ToolInstaller.DownloadUrlFor(ToolKind.YtDlp));

        if (RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
        {
            // Only x64/arm64 builds are published, so the installer must refuse instead of guessing.
            Assert.Throws<ToolInstallException>(() => ToolInstaller.DownloadUrlFor(ToolKind.Ffmpeg));
            return;
        }

        string url = ToolInstaller.DownloadUrlFor(ToolKind.Ffmpeg);
        Assert.StartsWith("https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-", url);
        Assert.EndsWith(OperatingSystem.IsWindows() ? ".zip" : ".tar.xz", url);
    }

    [Fact]
    public async Task Zip_extractor_finds_the_binary_wherever_it_sits_in_the_archive()
    {
        string archivePath = Path.Combine(_tempDir, "ffmpeg.zip");
        string member = ToolCatalog.FileNameFor(ToolKind.Ffmpeg);

        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            // A decoy entry first: the real binary sits two folders deep in the official archive.
            using (StreamWriter ignored = new(archive.CreateEntry("docs/readme.txt").Open()))
            {
                ignored.Write("not the binary");
            }

            using StreamWriter entry = new(archive.CreateEntry($"ffmpeg-master-latest/bin/{member}").Open());
            entry.Write("zip-ffmpeg");
        }

        string destination = Path.Combine(_tempDir, member);
        await new ZipToolArchiveExtractor().ExtractAsync(archivePath, member, destination, CancellationToken.None);

        Assert.Equal("zip-ffmpeg", File.ReadAllText(destination));
    }

    [Fact]
    public async Task Zip_extractor_reports_a_missing_binary()
    {
        string archivePath = Path.Combine(_tempDir, "empty.zip");
        using (ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            // An archive that does not contain the executable the app needs.
        }

        ToolInstallException error = await Assert.ThrowsAsync<ToolInstallException>(
            () => new ZipToolArchiveExtractor().ExtractAsync(
                archivePath,
                "ffmpeg.exe",
                Path.Combine(_tempDir, "out.exe"),
                CancellationToken.None));

        Assert.Contains("찾지 못했습니다", error.Message);
    }

    [Fact]
    public void Catalog_marks_only_the_tools_this_platform_needs()
    {
        IReadOnlyList<ToolRequirement> tools = ToolCatalog.Inspect();

        Assert.Equal(2, tools.Count);
        Assert.Equal(ToolKind.YtDlp, tools[0].Kind);
        Assert.Equal("yt-dlp", tools[0].DisplayName);
        Assert.True(tools[0].IsRequired);
        Assert.Equal(ToolCatalog.FileNameFor(ToolKind.YtDlp), tools[0].FileName);

        ToolRequirement ffmpeg = tools[1];
        Assert.Equal(ToolKind.Ffmpeg, ffmpeg.Kind);
        Assert.Equal("ffmpeg", ffmpeg.DisplayName);
        // Video playback and mp4 merging need ffmpeg on every platform.
        Assert.True(ffmpeg.IsRequired);

        // The shipped file names must match what the app searches for next to itself.
        Assert.EndsWith(OperatingSystem.IsWindows() ? ".exe" : string.Empty, tools[0].FileName);
    }

    [Fact]
    public void Skip_prompt_flag_round_trips_and_survives_a_settings_save()
    {
        Assert.False(AppSettings.GetFlag(AppSettings.UiSection, AppSettings.SkipToolInstallPromptKey, _tempDir));

        AppSettings.SetFlag(AppSettings.UiSection, AppSettings.SkipToolInstallPromptKey, true, _tempDir);
        Assert.True(AppSettings.GetFlag(AppSettings.UiSection, AppSettings.SkipToolInstallPromptKey, _tempDir));

        // Saving the 설정 dialog rewrites the same file; it must not drop the flag.
        AppSettings.Save("youtube-key", null, null, _tempDir);
        Assert.True(AppSettings.GetFlag(AppSettings.UiSection, AppSettings.SkipToolInstallPromptKey, _tempDir));

        AppSettings.SetFlag(AppSettings.UiSection, AppSettings.SkipToolInstallPromptKey, false, _tempDir);
        Assert.False(AppSettings.GetFlag(AppSettings.UiSection, AppSettings.SkipToolInstallPromptKey, _tempDir));
        Assert.DoesNotContain("SkipToolInstallPrompt", File.ReadAllText(AppSettings.GetLocalConfigPath(_tempDir)));
    }

    private static string ExpectedYtDlpUrl
    {
        get
        {
            const string baseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/";

            if (OperatingSystem.IsWindows())
            {
                return baseUrl + "yt-dlp.exe";
            }

            if (OperatingSystem.IsMacOS())
            {
                return baseUrl + "yt-dlp_macos";
            }

            return baseUrl + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "yt-dlp_linux_aarch64"
                : "yt-dlp_linux");
        }
    }

    private static HttpResponseMessage Bytes(byte[] payload)
        => new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };

    /// <summary>Reports progress on the calling thread so assertions cannot race the download.</summary>
    private sealed class ListProgress : IProgress<ToolInstallProgress>
    {
        public List<ToolInstallProgress> Values { get; } = new();

        public void Report(ToolInstallProgress value) => Values.Add(value);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(responder(request));
        }
    }

    /// <summary>Stands in for the platform extractor, recording what it was asked to unpack.</summary>
    private sealed class StubExtractor : IToolArchiveExtractor
    {
        public string? ArchivePath { get; private set; }

        public string? MemberName { get; private set; }

        public byte[]? ArchiveBytes { get; private set; }

        public Task ExtractAsync(
            string archivePath,
            string memberFileName,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            ArchivePath = archivePath;
            MemberName = memberFileName;

            // Extraction only makes sense once the archive has been downloaded completely.
            Assert.True(File.Exists(archivePath));
            ArchiveBytes = File.ReadAllBytes(archivePath);
            File.WriteAllText(destinationPath, "extracted-ffmpeg");
            return Task.CompletedTask;
        }
    }
}
