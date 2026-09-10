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

    [Fact]
    public void TempoNormal_StereoStream_PreservesLengthAndChannels()
    {
        var (stream, totalFrames) = StereoStream(1.0);

        using var provider = new SoundTouchStream(stream);
        Assert.Equal(2, provider.WaveFormat.Channels);

        var output = DrainFloats(provider);

        // Interleaved output; frame count must survive the SoundTouch round trip.
        Assert.InRange(output.Count, (int)(totalFrames * 2 * 0.85), (int)(totalFrames * 2 * 1.15));

        double leftRms = ChannelRms(output, 2, 0);
        double rightRms = ChannelRms(output, 2, 1);
        Assert.InRange(leftRms, 0.25, 0.45);   // 440 Hz @ 0.5 amplitude
        Assert.InRange(rightRms, 0.12, 0.24);  // 880 Hz @ 0.25 amplitude
    }

    [Fact]
    public void StereoStream_LargeDeviceBuffer_DoesNotThrow()
    {
        // 35280 bytes is what WaveOutEvent asks for with its default 100 ms latency.
        var (stream, totalFrames) = StereoStream(1.0);

        using var provider = new SoundTouchStream(stream);

        var output = DrainFloats(provider, 35280);

        Assert.InRange(output.Count, (int)(totalFrames * 2 * 0.85), (int)(totalFrames * 2 * 1.15));
    }

    private static double ChannelRms(List<float> interleaved, int channels, int channel)
    {
        double sum = 0;
        int count = 0;
        for (int i = channel; i < interleaved.Count; i += channels)
        {
            sum += interleaved[i] * interleaved[i];
            count++;
        }

        return count == 0 ? 0 : Math.Sqrt(sum / count);
    }

    private static List<float> DrainFloats(IWaveProvider provider, int bufferSize = 8192)
    {
        var output = new List<float>(SampleRate);
        var buffer = new byte[bufferSize];
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

    private static (MemoryFloatWaveStream Stream, int TotalFrames) StereoStream(double seconds)
    {
        int totalFrames = (int)(SampleRate * seconds);
        var samples = new float[totalFrames * 2];
        for (int i = 0; i < totalFrames; i++)
        {
            samples[i * 2] = (float)Math.Sin(2.0 * Math.PI * 440.0 * i / SampleRate) * 0.5f;
            samples[i * 2 + 1] = (float)Math.Sin(2.0 * Math.PI * 880.0 * i / SampleRate) * 0.25f;
        }

        return (new MemoryFloatWaveStream(samples, SampleRate, 2), totalFrames);
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
