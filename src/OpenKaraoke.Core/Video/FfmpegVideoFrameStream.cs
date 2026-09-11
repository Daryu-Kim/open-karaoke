using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using OpenKaraoke.Core.Audio;

namespace OpenKaraoke.Core.Video;

/// <summary>
/// Streams the raw BGRA frames of a file's first video stream through an ffmpeg subprocess
/// (<c>-f rawvideo -</c> on stdout). One instance owns one ffmpeg process: the karaoke audio
/// engine stays the playback clock, and this stream is paced against it.
/// Frames are forced to a constant rate, so frame N always lives at
/// <c>start + N / FramesPerSecond</c> on the song timeline.
/// </summary>
public sealed class FfmpegVideoFrameStream : IDisposable
{
    /// <summary>Refuses absurd frame buffers (8K BGRA is already ~132MB per frame).</summary>
    private const long MaxFrameBytes = 64L * 1024 * 1024;

    private readonly string _ffmpegPath;
    private readonly string _filePath;
    private readonly string? _scaleValue;

    private Process? _process;
    private Stream? _standardOutput;
    private Task<string>? _errorText;
    private TimeSpan _origin;
    private bool _disposed;

    private FfmpegVideoFrameStream(
        string ffmpegPath,
        string filePath,
        double frameRate,
        int width,
        int height,
        bool needsScaling)
    {
        _ffmpegPath = ffmpegPath;
        _filePath = filePath;
        Width = width;
        Height = height;
        FrameByteCount = width * height * 4;
        FramesPerSecond = frameRate;
        _scaleValue = needsScaling ? $"{width}:{height}" : null;
    }

    /// <summary>Frame width produced by the stream (after any downscale).</summary>
    public int Width { get; }

    /// <summary>Frame height produced by the stream (after any downscale).</summary>
    public int Height { get; }

    /// <summary>Bytes of one BGRA frame.</summary>
    public int FrameByteCount { get; }

    /// <summary>Constant output rate; frame N is shown at <c>Origin + N / FramesPerSecond</c>.</summary>
    public double FramesPerSecond { get; }

    /// <summary>Timeline position of the first frame currently being streamed.</summary>
    public TimeSpan Origin => _origin;

    /// <summary>Number of frames handed out since the last (re)start.</summary>
    public long FramesRead { get; private set; }

    /// <summary>Timeline position of the frame returned by the last successful read.</summary>
    public TimeSpan CurrentFrameTime => FramesRead == 0
        ? _origin
        : _origin + TimeSpan.FromSeconds((FramesRead - 1) / FramesPerSecond);

    /// <summary>First ffmpeg error line, if the process already reported one.</summary>
    public string? LastError
    {
        get
        {
            Task<string>? task = _errorText;
            if (task == null || !task.IsCompletedSuccessfully)
            {
                return null;
            }

            string text = task.Result;
            return string.IsNullOrWhiteSpace(text) ? null : Summarize(text);
        }
    }

    /// <summary>
    /// Starts a frame stream for <paramref name="filePath"/> at <paramref name="from"/>, downscaling
    /// to at most <paramref name="maxHeight"/>. Returns false with a Korean message when the file has
    /// no usable video stream or ffmpeg cannot be started.
    /// </summary>
    public static bool TryStart(
        string filePath,
        VideoMediaInfo info,
        int maxHeight,
        TimeSpan from,
        [NotNullWhen(true)] out FfmpegVideoFrameStream? stream,
        out string? error)
    {
        stream = null;
        error = null;

        if (!info.IsUsable)
        {
            error = "이 파일에는 영상이 없습니다.";
            return false;
        }

        string ffmpegPath = FfmpegLocator.Resolve();
        if (!File.Exists(ffmpegPath))
        {
            error = "영상 재생에 필요한 ffmpeg를 찾을 수 없습니다. 설치 후 다시 시도해 주세요.";
            return false;
        }

        (int width, int height) = VideoGeometry.FitToHeight(info.Width, info.Height, maxHeight);
        if (width <= 0 || height <= 0)
        {
            error = "영상 크기를 확인할 수 없습니다.";
            return false;
        }

        if ((long)width * height * 4 > MaxFrameBytes)
        {
            error = "영상 해상도가 너무 큽니다.";
            return false;
        }

        bool needsScaling = width != info.Width || height != info.Height;
        var candidate = new FfmpegVideoFrameStream(
            ffmpegPath,
            filePath,
            info.FramesPerSecond,
            width,
            height,
            needsScaling);

        if (!candidate.Spawn(ClampStart(from), info.FramesPerSecond, needsScaling))
        {
            candidate.Dispose();
            error = "영상 디코더(ffmpeg)를 시작하지 못했습니다.";
            return false;
        }

        stream = candidate;
        return true;
    }

    /// <summary>
    /// Blocking read of the next full frame into <paramref name="buffer"/> (which must hold
    /// <see cref="FrameByteCount"/> bytes). Returns false at the end of the stream or on failure;
    /// call <see cref="Dispose"/> to unblock a pending read.
    /// </summary>
    public bool TryReadFrame(byte[] buffer)
    {
        if (_disposed || _standardOutput == null)
        {
            return false;
        }

        if (buffer.Length < FrameByteCount)
        {
            throw new ArgumentException("Frame buffer is too small.", nameof(buffer));
        }

        ReadOnlySpan<byte> frame = ReadExactly(buffer);
        if (frame.Length < FrameByteCount)
        {
            return false;
        }

        FramesRead++;
        return true;
    }

    /// <summary>Kills the current decoder and re-spawns it at <paramref name="from"/> (seeks).</summary>
    public bool Restart(TimeSpan from)
    {
        if (_disposed)
        {
            return false;
        }

        StopProcess();
        return Spawn(ClampStart(from), FramesPerSecond, _scaleValue != null);
    }

    /// <summary>Stops the decoder process; safe to call from any thread.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopProcess();
    }

    private ReadOnlySpan<byte> ReadExactly(byte[] buffer)
    {
        Stream? output = _standardOutput;
        if (output == null)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        int read = 0;
        try
        {
            while (read < FrameByteCount)
            {
                int chunk = output.Read(buffer, read, FrameByteCount - read);
                if (chunk <= 0)
                {
                    break;
                }

                read += chunk;
            }
        }
        catch (Exception)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        return read == FrameByteCount ? buffer.AsSpan(0, FrameByteCount) : ReadOnlySpan<byte>.Empty;
    }

    private bool Spawn(TimeSpan from, double frameRate, bool needsScaling)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in BuildArguments(_filePath, from, frameRate, needsScaling ? _scaleValue : null))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return false;
            }
        }
        catch (Exception)
        {
            process.Dispose();
            return false;
        }

        _process = process;
        _standardOutput = process.StandardOutput.BaseStream;
        _errorText = process.StandardError.ReadToEndAsync();
        _origin = from > TimeSpan.Zero ? from : TimeSpan.Zero;
        FramesRead = 0;
        return true;
    }

    private void StopProcess()
    {
        Process? process = _process;
        _process = null;
        _standardOutput = null;
        _errorText = null;

        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2_000);
            }
        }
        catch (Exception)
        {
            // Already exited or not signalable; the frame pipe is closed either way.
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>
    /// Decodes only the first video stream to BGRA at a constant frame rate. <c>-ss</c> is an input
    /// option so seeking stays fast, and the output <c>-r</c> makes ffmpeg duplicate/drop frames to
    /// hit that rate exactly - the pacing contract the player relies on.
    /// No <c>-vsync</c>/<c>-fps_mode</c> is passed on purpose: those flags were renamed over the
    /// ffmpeg versions and recent builds reject <c>-vsync</c> outright, while the default for a
    /// rawvideo output is already constant frame rate.
    /// </summary>
    private static IEnumerable<string> BuildArguments(string filePath, TimeSpan from, double frameRate, string? scale)
    {
        yield return "-hide_banner";
        yield return "-nostdin";
        yield return "-v";
        yield return "error";

        if (from > TimeSpan.Zero)
        {
            yield return "-ss";
            yield return FormatSeconds(from.TotalSeconds);
        }

        yield return "-i";
        yield return filePath;
        yield return "-map";
        yield return "0:v:0";
        yield return "-an";
        yield return "-sn";
        yield return "-dn";

        if (scale != null)
        {
            yield return "-vf";
            yield return $"scale={scale}";
        }

        yield return "-pix_fmt";
        yield return "bgra";
        yield return "-r";
        yield return FormatSeconds(frameRate);
        yield return "-f";
        yield return "rawvideo";
        yield return "-";
    }

    private static TimeSpan ClampStart(TimeSpan from) => from < TimeSpan.Zero ? TimeSpan.Zero : from;

    private static string FormatSeconds(double seconds)
        => seconds.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Summarize(string text)
    {
        string trimmed = text.Trim();
        int newline = trimmed.IndexOfAny(['\r', '\n']);
        string line = newline < 0 ? trimmed : trimmed[..newline];
        return line.Length > 200 ? line[..200] : line;
    }
}
