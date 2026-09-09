using NAudio.Wave;
using Spectralis.Core.Audio;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class VariableSpeedSampleProviderTests
{
    /// <summary>Fixed-length 440 Hz sine, for measuring how many samples playback consumes.</summary>
    private sealed class SineSource : ISampleProvider
    {
        private readonly int _totalFrames;
        private int _frame;

        public SineSource(int sampleRate, int channels, double seconds)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            _totalFrames = (int)(sampleRate * seconds);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var ch = WaveFormat.Channels;
            var framesLeft = _totalFrames - _frame;
            var framesToWrite = Math.Min(framesLeft, count / ch);
            for (var i = 0; i < framesToWrite; i++)
            {
                var v = (float)(0.25 * Math.Sin(2 * Math.PI * 440 * (_frame + i) / WaveFormat.SampleRate));
                for (var c = 0; c < ch; c++)
                {
                    buffer[offset + (i * ch) + c] = v;
                }
            }

            _frame += framesToWrite;
            return framesToWrite * ch;
        }
    }

    private static (float[] Data, int Count) DrainAll(ISampleProvider provider)
    {
        var buffer = new float[4096];
        var collected = new List<float>();
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            collected.AddRange(buffer[..read]);
        }

        return (collected.ToArray(), collected.Count);
    }

    [Fact]
    public void Rate1_IsExactPassthrough()
    {
        var expected = DrainAll(new SineSource(44100, 2, 0.5));
        var actual = DrainAll(new VariableSpeedSampleProvider(new SineSource(44100, 2, 0.5), 1.0));

        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.Data, actual.Data);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.25)]
    [InlineData(2.0)]
    public void WaveFormat_MatchesSource_AtEveryRate(double rate)
    {
        var source = new SineSource(48000, 2, 0.2);
        var provider = new VariableSpeedSampleProvider(source, rate);

        Assert.Equal(48000, provider.WaveFormat.SampleRate);
        Assert.Equal(2, provider.WaveFormat.Channels);
    }

    [Fact]
    public void Rate2_ConsumesRoughlyHalfTheSamples()
    {
        var baseline = DrainAll(new SineSource(44100, 2, 1.0)).Count;
        var fast = DrainAll(new VariableSpeedSampleProvider(new SineSource(44100, 2, 1.0), 2.0)).Count;

        // ~half, allowing for resampler edge effects.
        Assert.InRange(fast, baseline * 0.4, baseline * 0.6);
    }

    [Fact]
    public void MoreThanTwoChannels_FallsBackToPassthrough()
    {
        var expected = DrainAll(new SineSource(44100, 6, 0.25));
        var actual = DrainAll(new VariableSpeedSampleProvider(new SineSource(44100, 6, 0.25), 1.5));

        Assert.Equal(expected.Data, actual.Data);
    }
}
