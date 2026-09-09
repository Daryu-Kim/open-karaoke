using NAudio.Wave;

namespace OpenKaraoke.Core.Audio;

/// <summary>
/// Real audio engine: decodes local m4a/mp3 through Windows Media Foundation, shifts key and
/// tempo in real time via <see cref="SoundTouchStream"/> and outputs through WaveOut.
/// </summary>
public sealed class KaraokeAudioPlayer : IKaraokePlayer
{
    public const int MinKeySemitones = -6;
    public const int MaxKeySemitones = 6;
    public const double MinTempo = 0.8;
    public const double MaxTempo = 1.2;

    private readonly object _gate = new();
    private MediaFoundationReader? _reader;
    private SoundTouchStream? _stream;
    private WaveOutEvent? _output;
    private int _keySemitones;
    private double _tempo = 1.0;
    private float _volume = 0.8f;
    private bool _disposed;

    public event EventHandler? PlaybackEnded;

    public bool IsOpen => _reader != null;

    public PlayerState State { get; private set; } = PlayerState.Stopped;

    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

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

    public int KeySemitones
    {
        get => _keySemitones;
        set
        {
            int clamped = Math.Clamp(value, MinKeySemitones, MaxKeySemitones);
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
            double clamped = Math.Clamp(value, MinTempo, MaxTempo);
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
                if (_output is not null)
                {
                    _output.Volume = clamped;
                }
            }
        }
    }

    public Task<bool> OpenAsync(string filePath)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.FromResult(false);
            }

            CloseLocked();

            if (!File.Exists(filePath))
            {
                return Task.FromResult(false);
            }

            try
            {
                var settings = new MediaFoundationReader.MediaFoundationReaderSettings
                {
                    RequestFloatOutput = true,
                };
                var reader = new MediaFoundationReader(filePath, settings);
                if (reader.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
                {
                    reader.Dispose();
                    return Task.FromResult(false);
                }

                _reader = reader;
                _stream = new SoundTouchStream(reader)
                {
                    KeySemitones = _keySemitones,
                    Tempo = _tempo,
                };
                State = PlayerState.Stopped;
                return Task.FromResult(true);
            }
            catch
            {
                CloseLocked();
                return Task.FromResult(false);
            }
        }
    }

    /// <summary>Starts or resumes playback. Returns false when no track/device is available.</summary>
    public bool Play()
    {
        lock (_gate)
        {
            if (_disposed || _reader is null || _stream is null)
            {
                return false;
            }

            if (State == PlayerState.Playing)
            {
                return true;
            }

            try
            {
                if (_output is null)
                {
                    var output = new WaveOutEvent
                    {
                        DesiredLatency = 150,
                        NumberOfBuffers = 2,
                        Volume = _volume,
                    };
                    output.PlaybackStopped += OnPlaybackStopped;
                    output.Init(_stream);
                    _output = output;
                }

                _output.Play();
                State = PlayerState.Playing;
                return true;
            }
            catch
            {
                if (_output is not null)
                {
                    _output.PlaybackStopped -= OnPlaybackStopped;
                    _output.Dispose();
                    _output = null;
                }

                State = PlayerState.Stopped;
                return false;
            }
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed || _output is null || State != PlayerState.Playing)
            {
                return;
            }

            _output.Pause();
            State = PlayerState.Paused;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            TearDownOutputLocked();
            State = PlayerState.Stopped;

            if (_stream is not null)
            {
                _stream.Reset();
            }

            RewindLocked();
        }
    }

    public void Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (_disposed || _reader is null)
            {
                return;
            }

            bool wasPlaying = State == PlayerState.Playing;
            TearDownOutputLocked();
            State = PlayerState.Stopped;

            if (_stream is not null)
            {
                _stream.Reset();
            }

            SeekLocked(position);

            if (wasPlaying)
            {
                Play();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            TearDownOutputLocked();
            _stream?.Dispose();
            _stream = null;
            _reader?.Dispose();
            _reader = null;
            State = PlayerState.Stopped;
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        bool ended;
        lock (_gate)
        {
            if (_disposed || _output is null || !ReferenceEquals(_output, sender))
            {
                return;
            }

            ended = e.Exception is null && State == PlayerState.Playing;
            TearDownOutputLocked();
            State = PlayerState.Stopped;

            if (ended && _stream is not null)
            {
                _stream.Reset();
                RewindLocked();
            }
        }

        if (ended)
        {
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void TearDownOutputLocked()
    {
        if (_output is null)
        {
            return;
        }

        _output.PlaybackStopped -= OnPlaybackStopped;
        _output.Dispose();
        _output = null;
    }

    private void CloseLocked()
    {
        TearDownOutputLocked();
        _stream?.Dispose();
        _stream = null;
        _reader?.Dispose();
        _reader = null;
        State = PlayerState.Stopped;
    }

    private void RewindLocked()
    {
        if (_reader is null)
        {
            return;
        }

        try
        {
            _reader.CurrentTime = TimeSpan.Zero;
        }
        catch
        {
            // Some Media Foundation sources do not support seeking; playback just restarts from
            // the current position instead.
        }
    }

    private void SeekLocked(TimeSpan position)
    {
        if (_reader is null)
        {
            return;
        }

        TimeSpan total = Duration;
        TimeSpan target = position < TimeSpan.Zero ? TimeSpan.Zero
            : position > total ? total
            : position;

        try
        {
            _reader.CurrentTime = target;
        }
        catch
        {
            // Non-seekable source: ignore the request.
        }
    }
}
