using OpenKaraoke.Core.Audio;
using SoundTouch;

namespace OpenKaraoke.App.Tests;

/// <summary>
/// Validates that the SoundTouch.Net DSP core behaves correctly on .NET 8 and that our
/// semitone/tempo parameters map to the expected audio changes. No audio device required.
/// </summary>
public class SoundTouchDspTests
{
    private const int SampleRate = 44100;

    [Fact]
    public void TempoNormal_OutputLength_MatchesInputWithinTolerance()
    {
        int frames = SampleRate; // 1 second
        var output = Process(new float[frames], tempo: 1.0, pitchSemitones: 0);

        Assert.InRange(output, frames * 0.9, frames * 1.1);
    }

    [Fact]
    public void TempoTwo_OutputIsAboutHalfTheInput()
    {
        int frames = SampleRate;
        var output = Process(new float[frames], tempo: 2.0, pitchSemitones: 0);

        Assert.InRange(output, frames * 0.4, frames * 0.6);
    }

    [Fact]
    public void TempoHalf_OutputIsAtLeastSixtyPercentLonger()
    {
        int frames = SampleRate;
        var output = Process(new float[frames], tempo: 0.5, pitchSemitones: 0);

        Assert.True(output > frames * 1.6, $"Expected longer output for tempo 0.5, got {output} frames.");
    }

    [Fact]
    public void PitchUpTwelveSemitones_DoublesFrequency()
    {
        var input = Sine(SampleRate, 440.0);
        var output = ProcessSamples(input, tempo: 1.0, pitchSemitones: 12);

        double estimatedHz = EstimateFrequencyHz(output);
        Assert.InRange(estimatedHz, 700, 1080);
    }

    [Fact]
    public void PitchDownTwelveSemitones_HalvesFrequency()
    {
        var input = Sine(SampleRate, 440.0);
        var output = ProcessSamples(input, tempo: 1.0, pitchSemitones: -12);

        double estimatedHz = EstimateFrequencyHz(output);
        Assert.InRange(estimatedHz, 170, 300);
    }

    [Fact]
    public void KeyLimitConstants_MatchPlayerContract()
    {
        Assert.Equal(-6, KaraokeAudioPlayer.MinKeySemitones);
        Assert.Equal(6, KaraokeAudioPlayer.MaxKeySemitones);
        Assert.Equal(0.8, KaraokeAudioPlayer.MinTempo);
        Assert.Equal(1.2, KaraokeAudioPlayer.MaxTempo);
    }

    private static long Process(float[] input, double tempo, int pitchSemitones)
    {
        var processor = NewProcessor(tempo, pitchSemitones);
        var inputSpan = new ReadOnlySpan<float>(input);
        processor.PutSamples(inputSpan, input.Length);

        var buffer = new float[SampleRate * 4];
        long total = 0;
        bool flushed = false;
        while (total < (long)SampleRate * 4)
        {
            var span = new Span<float>(buffer);
            int got = processor.ReceiveSamples(span, buffer.Length);
            if (got <= 0)
            {
                if (flushed)
                {
                    break;
                }

                processor.Flush();
                flushed = true;
                continue;
            }

            total += got;
        }

        return total;
    }

    private static float[] ProcessSamples(float[] input, double tempo, int pitchSemitones)
    {
        var processor = NewProcessor(tempo, pitchSemitones);
        var inputSpan = new ReadOnlySpan<float>(input);
        processor.PutSamples(inputSpan, input.Length);

        var output = new List<float>(SampleRate);
        var buffer = new float[SampleRate / 4];
        bool flushed = false;
        while (output.Count < SampleRate * 4)
        {
            var span = new Span<float>(buffer);
            int got = processor.ReceiveSamples(span, buffer.Length);
            if (got <= 0)
            {
                if (flushed)
                {
                    break;
                }

                processor.Flush();
                flushed = true;
                continue;
            }

            for (int i = 0; i < got; i++)
            {
                output.Add(buffer[i]);
            }
        }

        return output.ToArray();
    }

    private static SoundTouchProcessor NewProcessor(double tempo, int pitchSemitones) =>
        new()
        {
            SampleRate = SampleRate,
            Channels = 1,
            Tempo = tempo,
            PitchSemiTones = pitchSemitones,
        };

    private static float[] Sine(int frames, double frequencyHz)
    {
        var samples = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            samples[i] = (float)Math.Sin(2.0 * Math.PI * frequencyHz * i / SampleRate) * 0.5f;
        }

        return samples;
    }

    private static double EstimateFrequencyHz(float[] samples)
    {
        int signChanges = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            if ((samples[i - 1] < 0 && samples[i] >= 0) || (samples[i - 1] >= 0 && samples[i] < 0))
            {
                signChanges++;
            }
        }

        double durationSeconds = (double)samples.Length / SampleRate;
        return signChanges / 2.0 / durationSeconds;
    }
}
