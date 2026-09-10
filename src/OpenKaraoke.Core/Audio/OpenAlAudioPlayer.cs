using NAudio.Wave;
using Silk.NET.OpenAL;

namespace OpenKaraoke.Core.Audio;

/// <summary>
/// Linux/macOS audio engine: ffmpeg decodes the file into a temporary 32-bit float WAV, the
/// shared <see cref="SoundTouchStream"/> applies key/tempo changes and the bundled OpenAL Soft
/// backend streams the result to the default output device.
/// </summary>
/// <remarks>
/// Playback semantics mirror <see cref="KaraokeAudioPlayer"/> so the view models behave the same
/// on every platform: <see cref="OpenAsync"/> returns false when the file cannot be decoded,
/// <see cref="Play"/> returns false when no device is available and <see cref="PlaybackEnded"/>
/// is raised only when the track reaches its natural end.
/// </remarks>
public sealed class OpenAlAudioPlayer : IKaraokePlayer
{
    /// <summary>Buffered audio blocks; four of them keep playback smooth while staying responsive.</summary>
    private const int BufferCount = 4;

    /// <summary>Frames per buffer (≈ 93 ms at 44.1 kHz).</summary>
    private const int FramesPerBuffer = 4096;

    private const int PumpIdleMilliseconds = 5;

    private const string DeviceMissingMessage =
        "오디오 출력 장치를 열 수 없습니다. 사운드 장치 연결 상태를 확인해 주세요.";

    private readonly object _gate = new();
    private readonly FfmpegPcmDecoder _decoder = new();

    private ALContext? _alc;
    private AL? _al;
    private unsafe Device* _device;
    private unsafe Context* _context;
    private uint _source;
    private uint[] _buffers = [];

    private WaveFileReader? _reader;
    private SoundTouchStream? _stream;
    private byte[] _readScratch = [];
    private float[] _floatScratch = [];
    private short[] _pcmScratch = [];
    private int _channels = 2;
    private int _sampleRate = 44100;
    private BufferFormat _bufferFormat = BufferFormat.Stereo16;

    private Thread? _pump;
    private volatile bool _pumpStop;
    private bool _ended;

    private PlayerState _state = PlayerState.Stopped;
    private int _keySemitones;
    private double _tempo = 1.0;
    private float _volume = 0.8f;
    private string? _lastError;
    private bool _disposed;

    public event EventHandler? PlaybackEnded;

    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _reader is not null && _stream is not null;
            }
        }
    }

    public PlayerState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public TimeSpan Duration
    {
        get
        {
            lock (_gate)
            {
                return _reader?.TotalTime ?? TimeSpan.Zero;
            }
        }
    }

    public TimeSpan Position
    {
        get
        {
            lock (_gate)
            {
                return _reader?.CurrentTime ?? TimeSpan.Zero;
            }
        }
    }

    /// <summary>Korean description of the last failure (missing ffmpeg, broken file, no device).</summary>
    public string? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    public int KeySemitones
    {
        get => _keySemitones;
        set
        {
            int clamped = Math.Clamp(value, AudioRanges.MinKeySemitones, AudioRanges.MaxKeySemitones);
            lock (_gate)
            {
                _keySemitones = clamped;
                if (_stream is not null)
                {
                    _stream.KeySemitones = clamped;
                }
            }
        }
    }

    public double Tempo
    {
        get => _tempo;
        set
        {
            double clamped = Math.Clamp(value, AudioRanges.MinTempo, AudioRanges.MaxTempo);
            lock (_gate)
            {
                _tempo = clamped;
                if (_stream is not null)
                {
                    _stream.Tempo = clamped;
                }
            }
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            float clamped = Math.Clamp(value, 0f, 1f);
            lock (_gate)
            {
                _volume = clamped;
                if (_al is not null && _source != 0)
                {
                    _al.SetSourceProperty(_source, SourceFloat.Gain, clamped);
                }
            }
        }
    }

    public async Task<bool> OpenAsync(string filePath)
    {
        Thread? previousPump;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            previousPump = StopPumpLocked();
            HaltLocked();
            CloseTrackLocked();
            _lastError = null;
        }

        JoinPump(previousPump);

        FfmpegDecodeResult decode = await _decoder
            .DecodeAsync(filePath, CancellationToken.None)
            .ConfigureAwait(false);

        if (!decode.Success || decode.OutputPath is null)
        {
            lock (_gate)
            {
                _lastError = decode.ErrorMessage;
            }

            return false;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            try
            {
                var reader = new WaveFileReader(decode.OutputPath);
                if (reader.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat ||
                    reader.WaveFormat.BitsPerSample != 32)
                {
                    reader.Dispose();
                    _lastError = "지원하지 않는 오디오 형식입니다.";
                    return false;
                }

                int channels = reader.WaveFormat.Channels;
                int bytesPerFrame = reader.WaveFormat.BlockAlign;
                int sampleRate = reader.WaveFormat.SampleRate;

                _reader = reader;
                _stream = new SoundTouchStream(reader)
                {
                    KeySemitones = _keySemitones,
                    Tempo = _tempo,
                };

                _channels = channels;
                _sampleRate = sampleRate;
                _bufferFormat = channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16;
                _readScratch = new byte[FramesPerBuffer * bytesPerFrame];
                _floatScratch = new float[FramesPerBuffer * channels];
                _pcmScratch = new short[FramesPerBuffer * channels];
                _ended = false;
                _state = PlayerState.Stopped;
                return true;
            }
            catch (Exception ex)
            {
                CloseTrackLocked();
                _lastError = $"오디오 파일을 열 수 없습니다. ({ex.Message})";
                return false;
            }
        }
    }

    public bool Play()
    {
        lock (_gate)
        {
            if (_disposed || _reader is null || _stream is null)
            {
                return false;
            }

            if (_state == PlayerState.Playing && _pump is { IsAlive: true })
            {
                return true;
            }

            if (!EnsureOpenAlLocked())
            {
                _state = PlayerState.Stopped;
                return false;
            }

            try
            {
                if (_state == PlayerState.Paused)
                {
                    _al!.SourcePlay(_source);
                }
                else
                {
                    HaltLocked();
                    PrimeLocked();
                    _al!.SetSourceProperty(_source, SourceFloat.Gain, _volume);
                    _al.SourcePlay(_source);
                }

                _state = PlayerState.Playing;
                EnsurePumpLocked();
                return true;
            }
            catch (Exception ex)
            {
                _lastError = $"{DeviceMissingMessage} ({ex.Message})";
                _state = PlayerState.Stopped;
                return false;
            }
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed || _al is null || _source == 0 || _state != PlayerState.Playing)
            {
                return;
            }

            _al.SourcePause(_source);
            _state = PlayerState.Paused;
        }
    }

    public void Stop()
    {
        Thread? pump;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            pump = StopPumpLocked();
            HaltLocked();
            _stream?.Reset();
            SeekLocked(TimeSpan.Zero);
            _ended = false;
            _state = PlayerState.Stopped;
        }

        JoinPump(pump);
    }

    public void Seek(TimeSpan position)
    {
        Thread? pump;
        bool wasPlaying;
        lock (_gate)
        {
            if (_disposed || _reader is null)
            {
                return;
            }

            wasPlaying = _state == PlayerState.Playing;
            pump = StopPumpLocked();
            HaltLocked();
            _stream?.Reset();
            SeekLocked(position);
            _ended = false;
            _state = PlayerState.Stopped;
        }

        JoinPump(pump);

        if (wasPlaying)
        {
            Play();
        }
    }

    public void Dispose()
    {
        Thread? pump;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pump = StopPumpLocked();

            try
            {
                HaltLocked();
                ReleaseOpenAlLocked();
            }
            catch (Exception)
            {
                // The device is already gone; there is nothing left to release.
            }

            CloseTrackLocked();
            _state = PlayerState.Stopped;
        }

        JoinPump(pump);
        _decoder.Dispose();

        try
        {
            _al?.Dispose();
            _alc?.Dispose();
        }
        catch (Exception)
        {
            // Native backend teardown is best effort.
        }
    }

    private unsafe bool EnsureOpenAlLocked()
    {
        if (_al is not null && _source != 0)
        {
            return true;
        }

        try
        {
            _alc ??= ALContext.GetApi(true);
            _al ??= AL.GetApi(true);

            if (_device == null)
            {
                _device = _alc.OpenDevice(string.Empty);
                if (_device == null)
                {
                    _lastError = DeviceMissingMessage;
                    return false;
                }
            }

            if (_context == null)
            {
                _context = _alc.CreateContext(_device, null);
                _alc.MakeContextCurrent(_context);
            }

            if (_source == 0)
            {
                _source = _al.GenSource();
                _buffers = new uint[BufferCount];
                for (int i = 0; i < BufferCount; i++)
                {
                    _buffers[i] = _al.GenBuffer();
                }
            }

            return _source != 0;
        }
        catch (Exception ex)
        {
            _lastError = $"{DeviceMissingMessage} ({ex.Message})";
            return false;
        }
    }

    private unsafe void ReleaseOpenAlLocked()
    {
        if (_al is not null)
        {
            if (_source != 0)
            {
                _al.DeleteSource(_source);
                _source = 0;
            }

            foreach (uint buffer in _buffers)
            {
                if (buffer != 0)
                {
                    _al.DeleteBuffer(buffer);
                }
            }

            _buffers = [];
        }

        if (_alc is not null && _device != null)
        {
            if (_context != null)
            {
                _alc.DestroyContext(_context);
                _context = null;
            }

            _alc.CloseDevice(_device);
            _device = null;
        }
    }

    private void EnsurePumpLocked()
    {
        if (_pump is { IsAlive: true })
        {
            return;
        }

        _pumpStop = false;
        var pump = new Thread(PumpLoop)
        {
            IsBackground = true,
            Name = "OpenAL audio pump",
        };

        _pump = pump;
        pump.Start();
    }

    private Thread? StopPumpLocked()
    {
        _pumpStop = true;
        Thread? pump = _pump;
        _pump = null;
        return pump;
    }

    private static void JoinPump(Thread? pump)
    {
        if (pump is null || !pump.IsAlive || pump == Thread.CurrentThread)
        {
            return;
        }

        pump.Join(millisecondsTimeout: 2000);
    }

    /// <summary>Unqueues every buffer and stops the source, keeping the buffers allocated.</summary>
    private void HaltLocked()
    {
        if (_al is null || _source == 0)
        {
            return;
        }

        _al.SourceStop(_source);
        _al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out int queued);
        if (queued > 0)
        {
            var queuedBuffers = new uint[queued];
            _al.SourceUnqueueBuffers(_source, queuedBuffers);
        }

        _al.SetSourceProperty(_source, SourceFloat.Gain, _volume);
    }

    private void PrimeLocked()
    {
        if (_al is null || _source == 0)
        {
            return;
        }

        _ended = false;
        var primed = new List<uint>(BufferCount);
        foreach (uint buffer in _buffers)
        {
            if (_ended)
            {
                break;
            }

            if (FillBufferLocked(buffer) > 0)
            {
                primed.Add(buffer);
            }
        }

        if (primed.Count > 0)
        {
            _al.SourceQueueBuffers(_source, primed.ToArray());
        }
    }

    /// <summary>Reads one processed block of the DSP stream into an OpenAL buffer.</summary>
    private int FillBufferLocked(uint buffer)
    {
        if (_al is null || _stream is null)
        {
            return 0;
        }

        int bytes = _stream.Read(_readScratch, 0, _readScratch.Length);
        if (bytes <= 0)
        {
            _ended = true;
            return 0;
        }

        int samples = bytes / sizeof(float);
        Buffer.BlockCopy(_readScratch, 0, _floatScratch, 0, bytes);
        for (int i = 0; i < samples; i++)
        {
            _pcmScratch[i] = (short)Math.Clamp(_floatScratch[i] * 32767f, short.MinValue, short.MaxValue);
        }

        if (samples == _pcmScratch.Length)
        {
            _al.BufferData(buffer, _bufferFormat, _pcmScratch, _sampleRate);
        }
        else
        {
            _al.BufferData(buffer, _bufferFormat, _pcmScratch.AsSpan(0, samples).ToArray(), _sampleRate);
        }

        return samples / _channels;
    }

    /// <summary>
    /// Feeds the device from <see cref="SoundTouchStream"/> on a dedicated thread. OpenAL counts
    /// processed buffers in the queue, so the loop unqueues what the device finished and refills
    /// it immediately; playback ends once the stream is drained and the queue is empty.
    /// </summary>
    private void PumpLoop()
    {
        while (true)
        {
            bool ended;
            lock (_gate)
            {
                if (_pumpStop || _al is null || _source == 0)
                {
                    return;
                }

                _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);
                if (processed > 0)
                {
                    var freed = new uint[processed];
                    _al.SourceUnqueueBuffers(_source, freed);

                    var refill = new List<uint>(processed);
                    foreach (uint buffer in freed)
                    {
                        if (_ended)
                        {
                            continue;
                        }

                        if (FillBufferLocked(buffer) > 0)
                        {
                            refill.Add(buffer);
                        }
                    }

                    if (refill.Count > 0)
                    {
                        _al.SourceQueueBuffers(_source, refill.ToArray());
                    }
                }

                _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int alState);
                _al.GetSourceProperty(_source, GetSourceInteger.BuffersQueued, out int queued);

                // An underrun (or a device hiccup) stops the source; restarting is the documented recovery.
                if (!_ended && queued > 0 && alState != (int)SourceState.Playing &&
                    _state == PlayerState.Playing)
                {
                    _al.SourcePlay(_source);
                }

                ended = _ended && queued == 0;
                if (ended)
                {
                    _stream?.Reset();
                    SeekLocked(TimeSpan.Zero);
                    _ended = false;
                    _state = PlayerState.Stopped;
                }
            }

            if (ended)
            {
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
                return;
            }

            Thread.Sleep(PumpIdleMilliseconds);
        }
    }

    private void CloseTrackLocked()
    {
        _stream?.Dispose();
        _stream = null;
        _reader?.Dispose();
        _reader = null;
        _ended = false;
    }

    private void SeekLocked(TimeSpan position)
    {
        if (_reader is null)
        {
            return;
        }

        TimeSpan total = _reader.TotalTime;
        TimeSpan target = position < TimeSpan.Zero ? TimeSpan.Zero
            : position > total ? total
            : position;

        try
        {
            int blockAlign = Math.Max(1, _reader.WaveFormat.BlockAlign);
            long bytes = (long)(target.TotalSeconds * _reader.WaveFormat.AverageBytesPerSecond);
            _reader.Position = bytes / blockAlign * blockAlign;
        }
        catch (Exception)
        {
            // Non-seekable source: keep the current position.
        }
    }
}
