using System.Diagnostics;

namespace OpenKaraoke.Core.Audio;

/// <summary>Outcome of one ffmpeg decode attempt.</summary>
public sealed record FfmpegDecodeResult(bool Success, string? OutputPath, string? ErrorMessage);

/// <summary>
/// Decodes any source audio file (m4a, mp3, webm/opus, …) into an IEEE-float WAV inside a
/// temporary working directory using the system ffmpeg binary. The Linux playback path needs
/// this because Media Foundation is Windows-only and yt-dlp can return codecs no OS decoder
/// handles.
/// </summary>
public sealed class FfmpegPcmDecoder : IDisposable
{
    private const int DecodeTimeoutSeconds = 120;

    private readonly string _ffmpegPath;
    private readonly string _tempDirectory;
    private bool _disposed;

    /// <summary>
    /// Creates a decoder using <paramref name="ffmpegPath"/> or, when omitted, the binary found
    /// by <see cref="FfmpegLocator"/>.
    /// </summary>
    public FfmpegPcmDecoder(string? ffmpegPath = null)
    {
        _ffmpegPath = string.IsNullOrWhiteSpace(ffmpegPath) ? FfmpegLocator.Resolve() : ffmpegPath;
        _tempDirectory = Path.Combine(Path.GetTempPath(), "OpenKaraoke", "decode-" + Guid.NewGuid().ToString("N"));
    }

    /// <summary>Whether the ffmpeg binary could be located.</summary>
    public bool IsAvailable => File.Exists(_ffmpegPath);

    /// <summary>Korean message shown when ffmpeg is missing on this machine.</summary>
    public static string ToolMissingMessage =>
        "오디오 변환 도구(ffmpeg)를 찾을 수 없습니다. Linux에서는 'sudo apt install ffmpeg'로 설치한 뒤 다시 시도해 주세요.";

    public async Task<FfmpegDecodeResult> DecodeAsync(string inputPath, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return new FfmpegDecodeResult(false, null, "재생 준비가 취소되었습니다.");
        }

        if (!IsAvailable)
        {
            return new FfmpegDecodeResult(false, null, ToolMissingMessage);
        }

        if (!File.Exists(inputPath))
        {
            return new FfmpegDecodeResult(false, null, "재생할 파일을 찾을 수 없습니다.");
        }

        string outputPath;
        try
        {
            Directory.CreateDirectory(_tempDirectory);
            outputPath = Path.Combine(_tempDirectory, "decoded.wav");
        }
        catch (Exception ex)
        {
            return new FfmpegDecodeResult(false, null, $"임시 폴더를 만들 수 없습니다. ({ex.Message})");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in BuildArguments(inputPath, outputPath))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new FfmpegDecodeResult(false, null, ToolMissingMessage);
            }
        }
        catch (Exception)
        {
            return new FfmpegDecodeResult(false, null, ToolMissingMessage);
        }

        Task<string> errorText = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(DecodeTimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new FfmpegDecodeResult(false, null, "오디오 변환 시간이 초과되었습니다.");
        }

        string details = await errorText.ConfigureAwait(false);
        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            string hint = string.IsNullOrWhiteSpace(details) ? string.Empty : $" ({FirstLine(details)})";
            return new FfmpegDecodeResult(false, null, $"오디오 파일을 변환하지 못했습니다.{hint}");
        }

        return new FfmpegDecodeResult(true, outputPath, null);
    }

    /// <summary>Deletes the temporary decode folder.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch (Exception)
        {
            // Temporary files are cleaned up by the OS later if they are still locked.
        }
    }

    /// <summary>
    /// Decodes only the first audio stream to 32-bit float PCM; <c>-ac 2</c> guarantees a
    /// stereo (or mono) layout, which is all OpenAL's Mono16/Stereo16 formats accept.
    /// </summary>
    private static string[] BuildArguments(string inputPath, string outputPath) =>
    [
        "-hide_banner",
        "-v", "error",
        "-nostdin",
        "-y",
        "-i", inputPath,
        "-map", "0:a:0",
        "-vn",
        "-ac", "2",
        "-acodec", "pcm_f32le",
        "-f", "wav",
        outputPath,
    ];

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // The process already exited or cannot be signalled; nothing else to do.
        }
    }

    private static string FirstLine(string text)
    {
        string trimmed = text.Trim();
        int newline = trimmed.IndexOfAny(['\r', '\n']);
        string line = newline < 0 ? trimmed : trimmed[..newline];
        return line.Length > 200 ? line[..200] : line;
    }
}
