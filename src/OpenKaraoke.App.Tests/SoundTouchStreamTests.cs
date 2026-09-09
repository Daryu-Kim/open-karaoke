using NAudio.Wave;
using OpenKaraoke.Core.Audio;

namespace OpenKaraoke.App.Tests;

public class SoundTouchStreamTests
{
    private const int SampleRate = 44100;

    [Fact]
    public void Constructor_RequiresIeeeFloatSource()
    {
        var pcmStream = new MemoryPcm16WaveStream(new byte[SampleRate * 2], SampleRate, 1);

        Assert.Throws<ArgumentException>(() => new SoundTouchStream(pcmStream));
    }

    [Fact]
    public void TempoNormal_WholeStream_CompletesWithSimilarLength()
    {
        var (stream, totalFrames) = SineStream(1.0);

        using var provider = new SoundTouchStream(stream);
        Assert.Equal(WaveFormatEncoding.IeeeFloat, provider.WaveFormat.Encoding);
        Assert.Equal(1, provider.WaveFormat.Channels);

        var output = DrainFloats(provider);

        Assert.InRange(output.Count, (int)(totalFrames * 0.85), (int)(totalFrames * 1.15));
        Assert.Equal(0, ReadOnce(provider)); // EOF reached
    }

    [Fact]
    public void TempoOneTwo_WholeStream_ShorterThanSource()
    {
        var (stream, totalFrames) = SineStream(1.0);

        using var provider = new SoundTouchStream(stream)
        {
            Tempo = 1.2,
        };

        var output = DrainFloats(provider);

        Assert.True(output.Count < totalFrames * 0.95, $"Expected faster output to be shorter, got {output.Count}.");
    }

    [Fact]
    public void ChangingParametersMidStream_DoesNotBreakTheStream()
    {
        var (stream, _) = SineStream(2.0);

        using var provider = new SoundTouchStream(stream);

        var buffer = new byte[4096];
        int first = provider.Read(buffer, 0, buffer.Length);
        Assert.True(first > 0);

        provider.KeySemitones = 3;
        provider.Tempo = 1.1;

        var output = DrainFloats(provider);
        Assert.True(output.Count > 0);
    }

    [Fact]
    public void AfterReset_StreamCanBeDrainedAgain()
    {
        var (stream, totalFrames) = SineStream(1.0);

        using var provider = new SoundTouchStream(stream);

        var first = DrainFloats(provider);
        Assert.True(first.Count > 0);

        stream.Position = 0;
        provider.Reset();

        var second = DrainFloats(provider);
        Assert.True(second.Count > 0);
        Assert.InRange(second.Count, (int)(totalFrames * 0.85), (int)(totalFrames * 1.15));
    }

    [Fact]
    public void Reset_ClearsFinishedState()
    {
        var (stream, _) = SineStream(0.5);

        using var provider = new SoundTouchStream(stream);
        DrainFloats(provider);
        Assert.Equal(0, ReadOnce(provider));

        stream.Position = 0;
        provider.Reset();

        Assert.True(ReadOnce(provider) > 0);
    }

    private static List<float> DrainFloats(IWaveProvider provider)
    {
        var output = new List<float>(SampleRate);
        var buffer = new byte[8192];
        while (true)
        {
            int read = provider.Read(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            for (int i = 0; i < read; i += 4)
            {
                output.Add(BitConverter.ToSingle(buffer, i));
            }
        }

        return output;
    }

    private static int ReadOnce(IWaveProvider provider)
    {
        var buffer = new byte[8192];
        return provider.Read(buffer, 0, buffer.Length);
    }

    private static (MemoryFloatWaveStream Stream, int TotalFrames) SineStream(double seconds)
    {
        int totalFrames = (int)(SampleRate * seconds);
        var samples = new float[totalFrames];
        for (int i = 0; i < totalFrames; i++)
        {
            samples[i] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / SampleRate) * 0.5f;
        }

        return (new MemoryFloatWaveStream(samples, SampleRate, 1), totalFrames);
    }

    /// <summary>In-memory IEEE-float <see cref="WaveStream"/> for offline tests.</summary>
    private sealed class MemoryFloatWaveStream : WaveStream
    {
        private readonly float[] _samples;
        private readonly WaveFormat _format;
        private long _positionBytes;

        public MemoryFloatWaveStream(float[] samples, int sampleRate, int channels)
        {
            _samples = samples;
            _format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        public override WaveFormat WaveFormat => _format;

        public override long Length => (long)_samples.Length * 4;

        public override long Position
        {
            get => _positionBytes;
            set => _positionBytes = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = (int)Math.Min(count, Length - _positionBytes);
            int availableFloats = available / 4;
            if (availableFloats <= 0)
            {
                return 0;
            }

            Buffer.BlockCopy(_samples, (int)(_positionBytes / 4) * 4, buffer, offset, availableFloats * 4);
            _positionBytes += availableFloats * 4L;
            return availableFloats * 4;
        }
    }

    private sealed class MemoryPcm16WaveStream : WaveStream
    {
        private readonly byte[] _bytes;
        private readonly WaveFormat _format;
        private long _position;

        public MemoryPcm16WaveStream(byte[] bytes, int sampleRate, int channels)
        {
            _bytes = bytes;
            _format = new WaveFormat(sampleRate, 16, channels);
        }

        public override WaveFormat WaveFormat => _format;

        public override long Length => _bytes.Length;

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int available = (int)Math.Min(count, _bytes.Length - _position);
            Buffer.BlockCopy(_bytes, (int)_position, buffer, offset, available);
            _position += available;
            return available;
        }
    }
}
