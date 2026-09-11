using Avalonia.Threading;
using OpenKaraoke.Core.Video;
using OpenKaraoke.Desktop.Diagnostics;

namespace OpenKaraoke.Desktop.Media;

/// <summary>
/// Paces decoded video frames against the karaoke audio engine, which stays the master clock.
/// <c>IKaraokePlayer.Position</c> always reports the song timeline even when the pitch/tempo engine
/// runs at 105%, so a frame is simply released when the clock reaches its timestamp - no matter how
/// fast or slow the audio is played.
/// Decoding runs on its own thread; frames are handed to the UI thread through a single slot, so a
/// busy UI drops frames instead of building a backlog. The decoder process only runs while a surface
/// is attached, which keeps the CPU free while the karaoke screen is hidden.
/// </summary>
public sealed class VideoPlayback : IDisposable
{
    /// <summary>Output is downscaled to this height at most, so a 4K source cannot saturate the pipe.</summary>
    public const int MaxOutputHeight = 1080;

    private const double LateFrameSeconds = 0.15;
    private const int MaxConsecutiveDrops = 60;
    private const double BackwardJumpSeconds = 1.0;
    private const int MaxWaitMilliseconds = 15;

    private readonly Func<TimeSpan> _position;
    private readonly Func<bool> _isPlayingAudio;
    private readonly object _frameLock = new();
    private readonly object _decoderLock = new();

    private FfmpegVideoFrameStream? _stream;
    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private VideoSurface? _surface;
    private byte[]? _pendingFrame;
    private int _pendingWidth;
    private int _pendingHeight;
    private int _pendingGeneration;
    private bool _pendingReady;
    private bool _presentQueued;
    private int _generation;
    private VideoMediaInfo? _mediaInfo;
    private bool _disposed;

    public VideoPlayback(Func<TimeSpan> position, Func<bool> isPlayingAudio)
    {
        _position = position;
        _isPlayingAudio = isPlayingAudio;
    }

    /// <summary>Raised (on the UI thread) whenever <see cref="HasVideo"/>/<see cref="Message"/> change.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Message shown when the song was downloaded as audio only.</summary>
    public static string NoVideoMessage =>
        "이 곡에는 가사 화면(영상)이 없습니다.\n"
        + "내 라이브러리에서 '영상 다시 받기'를 누르면 영상을 받아 다시 저장할 수 있습니다.";

    /// <summary>File the karaoke screen is following; null when no song is open.</summary>
    public string? MediaPath { get; private set; }

    /// <summary>True while the file is being probed and decoded.</summary>
    public bool IsPreparing { get; private set; }

    /// <summary>True when the loaded file has a playable video stream.</summary>
    public bool HasVideo { get; private set; }

    /// <summary>Korean status text for the karaoke screen; null while frames are flowing.</summary>
    public string? Message { get; private set; }

    /// <summary>
    /// Loads a new song: stops the current decoder and probes the file in the background.
    /// Attaching a surface afterwards starts the frame stream.
    /// </summary>
    public void Load(string? filePath)
    {
        if (_disposed)
        {
            return;
        }

        Stop();

        string? path = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
        MediaPath = path;

        if (path is null || !File.Exists(path))
        {
            SetState(preparing: false, hasVideo: false, message: null);
            return;
        }

        SetState(preparing: true, hasVideo: false, message: "가사 화면을 준비하는 중입니다...");
        _ = Task.Run(() => Prepare(path));
    }

    /// <summary>Forgets the current song and stops decoding.</summary>
    public void Stop()
    {
        StopDecoder();
        DiscardPendingFrame();
        MediaPath = null;
        _mediaInfo = null;
        _surface?.Clear();
        SetState(preparing: false, hasVideo: false, message: null);
    }

    /// <summary>
    /// Points the decoder at a surface (the in-app panel or the customer monitor window).
    /// Passing null pauses decoding until a surface is available again.
    /// </summary>
    public void Attach(VideoSurface? surface)
    {
        if (_disposed || ReferenceEquals(_surface, surface))
        {
            return;
        }

        _surface = surface;

        if (surface is null)
        {
            StopDecoder();
            return;
        }

        surface.Clear();
        StartDecoder();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void Prepare(string path)
    {
        try
        {
            ProbeAndStartDecoder(path);
        }
        catch (Exception ex)
        {
            AppLog.Write($"[video] 가사 화면 준비 실패: {ex}");
            SetState(preparing: false, hasVideo: false, message: "가사 화면을 준비하지 못했습니다.");
        }
    }

    private void ProbeAndStartDecoder(string path)
    {
        VideoMediaInfo info = FfmpegMediaProbe.Probe(path);
        if (_disposed || !string.Equals(MediaPath, path, StringComparison.Ordinal))
        {
            return;
        }

        if (!info.IsUsable)
        {
            AppLog.Write($"[video] 영상 스트림 없음: {Path.GetFileName(path)}");
            SetState(preparing: false, hasVideo: false, message: NoVideoMessage);
            return;
        }

        _mediaInfo = info;
        AppLog.Write($"[video] 영상 스트림 확인: {info.Width}x{info.Height}, {info.FramesPerSecond:0.##}fps ({Path.GetFileName(path)})");
        SetState(preparing: false, hasVideo: true, message: null);

        // StartDecoder is a no-op without a surface; when one is already attached (the panel keeps
        // its surface across songs) this is what actually gets the next song's frames moving.
        StartDecoder();
    }

    private void StartDecoder()
    {
        lock (_decoderLock)
        {
            if (_disposed || _surface is null || !HasVideo || _stream is not null || MediaPath is null || _mediaInfo is null)
            {
                return;
            }

            TimeSpan from = _position();
            if (!FfmpegVideoFrameStream.TryStart(MediaPath, _mediaInfo, MaxOutputHeight, from, out FfmpegVideoFrameStream? stream, out string? error))
            {
                AppLog.Write($"[video] 디코더 시작 실패: {error}");
                SetState(IsPreparing, HasVideo, string.IsNullOrWhiteSpace(error) ? "영상을 재생할 수 없습니다." : error);
                return;
            }

            _stream = stream;

            var cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;
            _cts = cts;

            var thread = new Thread(() => RunDecoder(stream, token))
            {
                IsBackground = true,
                Name = "video-decode",
                Priority = ThreadPriority.AboveNormal,
            };
            _thread = thread;
            thread.Start();
        }
    }

    private void StopDecoder()
    {
        Thread? thread;
        CancellationTokenSource? cts;
        FfmpegVideoFrameStream? stream;

        lock (_decoderLock)
        {
            thread = _thread;
            cts = _cts;
            stream = _stream;
            _thread = null;
            _cts = null;
            _stream = null;
        }

        cts?.Cancel();
        stream?.Dispose();

        if (thread is not null && thread != Thread.CurrentThread)
        {
            thread.Join(1_500);
        }

        cts?.Dispose();
    }

    /// <summary>
    /// Releases one decoded frame after another whenever the audio clock reaches its timestamp.
    /// A backwards jump means the song was restarted (되감기), which re-opens ffmpeg at that point.
    /// </summary>
    private void RunDecoder(FfmpegVideoFrameStream stream, CancellationToken token)
    {
        int width = stream.Width;
        int height = stream.Height;
        var frame = new byte[stream.FrameByteCount];
        int drops = 0;

        try
        {
            if (!stream.TryReadFrame(frame))
            {
                ReportDecoderFailure(stream);
                return;
            }

            TimeSpan frameTime = stream.CurrentFrameTime;

            while (!token.IsCancellationRequested)
            {
                TimeSpan position = _position();

                if (position < frameTime - TimeSpan.FromSeconds(BackwardJumpSeconds))
                {
                    if (!stream.Restart(position) || !stream.TryReadFrame(frame))
                    {
                        ReportDecoderFailure(stream);
                        return;
                    }

                    frameTime = stream.CurrentFrameTime;
                    drops = 0;
                    continue;
                }

                if (!_isPlayingAudio())
                {
                    Thread.Sleep(MaxWaitMilliseconds);
                    continue;
                }

                double late = (position - frameTime).TotalSeconds;

                if (late < 0)
                {
                    Thread.Sleep(WaitMilliseconds(-late));
                    continue;
                }

                if (late > LateFrameSeconds && drops < MaxConsecutiveDrops)
                {
                    // Decoder is behind (startup, rewind, loaded machine): skip frames instead of
                    // showing stale lyrics, but never skip for longer than the drop budget.
                    drops++;
                    if (!stream.TryReadFrame(frame))
                    {
                        break;
                    }

                    frameTime = stream.CurrentFrameTime;
                    continue;
                }

                drops = 0;
                Publish(frame, width, height);

                if (!stream.TryReadFrame(frame))
                {
                    break;
                }

                frameTime = stream.CurrentFrameTime;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"[video] 디코더 예외: {ex}");
        }
        finally
        {
            ClearDecoderState(stream);
            stream.Dispose();
        }
    }

    private void ReportDecoderFailure(FfmpegVideoFrameStream stream)
    {
        if (_disposed || !HasVideo)
        {
            return;
        }

        string? error = stream.LastError;
        AppLog.Write($"[video] 프레임 스트림 종료: {error ?? "정상 종료(영상 끝)"}");

        if (error is null)
        {
            // Nothing on ffmpeg's stderr means the video simply ran out, which normally happens on
            // the song's last frame. Leave the picture on screen instead of flashing an error.
            return;
        }

        SetState(IsPreparing, HasVideo, error);
    }

    /// <summary>
    /// Frees the decoder slot once the worker exits (or dies), so a later Attach/Load can spawn a
    /// fresh process instead of being blocked by a stale stream reference.
    /// </summary>
    private void ClearDecoderState(FfmpegVideoFrameStream stream)
    {
        lock (_decoderLock)
        {
            if (ReferenceEquals(_stream, stream))
            {
                _stream = null;
                _thread = null;
                _cts = null;
            }
        }
    }

    /// <summary>Hands the newest frame to the UI thread; older pending frames are overwritten.</summary>
    private void Publish(byte[] frame, int width, int height)
    {
        lock (_frameLock)
        {
            if (_pendingFrame is null || _pendingFrame.Length < frame.Length)
            {
                _pendingFrame = new byte[frame.Length];
            }

            Buffer.BlockCopy(frame, 0, _pendingFrame, 0, frame.Length);
            _pendingWidth = width;
            _pendingHeight = height;
            _pendingGeneration = _generation;
            _pendingReady = true;

            if (_presentQueued)
            {
                return;
            }

            _presentQueued = true;
        }

        Dispatcher.UIThread.Post(DrainPending, DispatcherPriority.Render);
    }

    private void DrainPending()
    {
        lock (_frameLock)
        {
            _presentQueued = false;
            if (!_pendingReady || _pendingFrame is null || _pendingGeneration != _generation)
            {
                _pendingReady = false;
                return;
            }

            _pendingReady = false;
            _surface?.Present(_pendingFrame, _pendingWidth, _pendingHeight);
        }
    }

    /// <summary>
    /// Drops frames that are still waiting for the UI thread. Without this the last frame of a
    /// finished song could be painted after the surface was already cleared.
    /// </summary>
    private void DiscardPendingFrame()
    {
        lock (_frameLock)
        {
            _generation++;
            _pendingReady = false;
        }
    }

    private static int WaitMilliseconds(double seconds)
    {
        int milliseconds = (int)Math.Ceiling(seconds * 1_000);
        return Math.Clamp(milliseconds, 1, MaxWaitMilliseconds);
    }

    private void SetState(bool preparing, bool hasVideo, string? message)
    {
        IsPreparing = preparing;
        HasVideo = hasVideo;
        Message = message;
        Notify();
    }

    private void Notify()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            Dispatcher.UIThread.Post(() => StateChanged?.Invoke(this, EventArgs.Empty));
        }
    }
}
