using NAudio.Wave;
using SoundTouch;

namespace OpenKaraoke.Core.Audio;

/// <summary>
/// Bridges an IEEE-float <see cref="WaveStream"/> (e.g. a MediaFoundationReader opened with
/// float output) into <see cref="SoundTouchProcessor"/> and exposes the processed stream as an
/// <see cref="IWaveProvider"/> for NAudio output devices. Pitch and tempo can be changed live
/// while data is flowing; SoundTouch is designed for real-time parameter updates.
/// </summary>
/// <remarks>
/// <see cref="Read"/> is expected to be called from a single NAudio output thread. Parameter
/// setters are thread-safe and lock against <see cref="Read"/>.
/// </remarks>
public sealed class SoundTouchStream : IWaveProvider, IDisposable
{
    private readonly object _gate = new();
    private readonly SoundTouchProcessor _soundTouch = new();
    private readonly WaveStream _source;
    private readonly WaveFormat _waveFormat;
    private readonly int _channels;
    private readonly int _bytesPerFrame;
    private readonly byte[] _readBuffer;
    private readonly float[] _readFloats;

    private float[] _scratch = new float[0];
    private bool _sourceEnded;
    private bool _flushed;
    private bool _finished;
    private bool _disposed;

    public SoundTouchStream(WaveStream source)
    {
        if (source.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
        {
            throw new ArgumentException("Source must use IEEE float encoding (32-bit).", nameof(source));
        }

        _source = source;
        _waveFormat = source.WaveFormat;
        _channels = source.WaveFormat.Channels;
        _bytesPerFrame = source.WaveFormat.BlockAlign;
        _soundTouch.SampleRate = source.WaveFormat.SampleRate;
        _soundTouch.Channels = _channels;

        // One source read pulls up to this many frames; must stay below WaveOut request sizes so
        // a single read can always satisfy a request after a flush.
        int framesPerRead = 2048;
        _readBuffer = new byte[framesPerRead * _bytesPerFrame];
        _readFloats = new float[framesPerRead * _channels];
    }

    public WaveFormat WaveFormat => _waveFormat;

    /// <summary>Key shift in semitones applied live.</summary>
    public int KeySemitones
    {
        set
        {
            lock (_gate)
            {
                _soundTouch.PitchSemiTones = value;
            }
        }
    }

    /// <summary>Tempo ratio (1.0 = normal) applied live.</summary>
    public double Tempo
    {
        set
        {
            lock (_gate)
            {
                _soundTouch.Tempo = value;
            }
        }
    }

    /// <summary>Resets the processing pipeline so a (repositioned) source can be played again.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _soundTouch.Clear();
            _sourceEnded = false;
            _flushed = false;
            _finished = false;
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            if (_disposed || _finished)
            {
                return 0;
            }

            EnsureScratch(count);
            int framesWanted = count / _bytesPerFrame;
            int bytesProduced = 0;

            while (bytesProduced < count)
            {
                int framesGot = ReceiveFrames(_scratch, framesWanted - bytesProduced / _bytesPerFrame);
                if (framesGot > 0)
                {
                    Buffer.BlockCopy(_scratch, 0, buffer, offset + bytesProduced, framesGot * _bytesPerFrame);
                    bytesProduced += framesGot * _bytesPerFrame;
                    continue;
                }

                if (!_sourceEnded)
                {
                    FeedSource();
                    if (!_sourceEnded)
                    {
                        continue;
                    }
                }

                if (!_flushed)
                {
                    _soundTouch.Flush();
                    _flushed = true;
                    continue;
                }

                // Source exhausted and SoundTouch fully drained.
                _finished = true;
                break;
            }

            return bytesProduced;
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
            _soundTouch.Clear();
        }
    }

    private void EnsureScratch(int count)
    {
        int neededFloats = count / 4;
        if (_scratch.Length < neededFloats)
        {
            _scratch = new float[neededFloats];
        }
    }

    private void FeedSource()
    {
        int bytesRead = _source.Read(_readBuffer, 0, _readBuffer.Length);
        if (bytesRead <= 0)
        {
            _sourceEnded = true;
            return;
        }

        int framesRead = bytesRead / _bytesPerFrame;
        Buffer.BlockCopy(_readBuffer, 0, _readFloats, 0, bytesRead);
        var input = new ReadOnlySpan<float>(_readFloats, 0, framesRead * _channels);
        _soundTouch.PutSamples(input, framesRead);
    }

    private int ReceiveFrames(float[] target, int maxFrames)
    {
        // SoundTouch counts "samples" in frames: one sample holds every channel, and the
        // target span must be able to hold maxFrames * channels interleaved floats.
        var span = new Span<float>(target, 0, maxFrames * _channels);
        return _soundTouch.ReceiveSamples(span, maxFrames);
    }
}
