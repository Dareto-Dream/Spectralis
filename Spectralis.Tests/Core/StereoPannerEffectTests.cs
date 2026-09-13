using NAudio.Wave;
using Spectralis.Core.Audio.Effects;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class StereoPannerEffectTests
{
    private sealed class ConstantStereoProvider : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i++)
            {
                buffer[offset + i] = 0.5f;
            }

            return count;
        }
    }

    [Fact]
    public void EvaluatePan_AtExactNodes_ReturnsThatNodesPan()
    {
        var points = StereoPannerEffect.DefaultPoints; // (0,0) (0.25,0.8) (0.5,0) (0.75,-0.8)

        Assert.Equal(0f, StereoPannerEffect.EvaluatePan(points, 0f), 3);
        Assert.Equal(0.8f, StereoPannerEffect.EvaluatePan(points, 0.25f), 3);
        Assert.Equal(0f, StereoPannerEffect.EvaluatePan(points, 0.5f), 3);
        Assert.Equal(-0.8f, StereoPannerEffect.EvaluatePan(points, 0.75f), 3);
    }

    [Fact]
    public void EvaluatePan_BetweenNodes_LinearlyInterpolates()
    {
        var points = StereoPannerEffect.DefaultPoints;

        // Halfway between (0, 0) and (0.25, 0.8).
        Assert.Equal(0.4f, StereoPannerEffect.EvaluatePan(points, 0.125f), 3);
    }

    [Fact]
    public void EvaluatePan_PastLastNode_WrapsToFirstNode()
    {
        var points = StereoPannerEffect.DefaultPoints;

        // Halfway between (0.75, -0.8) and the wrapped (1.0 == 0.0, 0).
        Assert.Equal(-0.4f, StereoPannerEffect.EvaluatePan(points, 0.875f), 3);
    }

    [Fact]
    public void EvaluatePan_SinglePoint_ReturnsItsPanEverywhere()
    {
        PanPoint[] points = [new(0.3f, 0.6f)];

        Assert.Equal(0.6f, StereoPannerEffect.EvaluatePan(points, 0f));
        Assert.Equal(0.6f, StereoPannerEffect.EvaluatePan(points, 0.9f));
    }

    [Fact]
    public void EvaluatePan_UnsortedInput_StillInterpolatesCorrectly()
    {
        // Same nodes as the default loop, but written out of time order.
        PanPoint[] points = [new(0.75f, -0.8f), new(0.25f, 0.8f), new(0f, 0f), new(0.5f, 0f)];

        Assert.Equal(0.8f, StereoPannerEffect.EvaluatePan(points, 0.25f), 3);
    }

    [Fact]
    public void AutoPan_MovesGainAcrossLoop_ForConstantInput()
    {
        // Bpm=220 is the max, so with Bars=1 the loop is 4 beats at 220bpm ≈ 1.09s
        // — the shortest loop the effect can produce.
        var effect = new StereoPannerEffect { AutoPanEnabled = true, Bars = 1, Bpm = 220 };
        var provider = effect.Wrap(new ConstantStereoProvider());

        const double loopSeconds = 1 * 4.0 * (60.0 / 220.0);
        const int sampleRate = 44100;

        // Read out to just past the loop's t=0.25 node (pan swung hard toward +0.8/right).
        var totalFrames = (int)(loopSeconds * sampleRate * 0.4);
        var buffer = new float[totalFrames * 2];
        var read = provider.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
        Assert.All(buffer, sample => Assert.InRange(sample, -1f, 1f));

        var startLeft = buffer[0]; // phase ≈ 0, pan = 0
        var peakRightFrame = (int)(loopSeconds * 0.25 * sampleRate); // phase ≈ 0.25, pan = +0.8
        var peakLeftSample = buffer[peakRightFrame * 2];

        Assert.True(
            startLeft - peakLeftSample > 0.15f,
            $"left-channel level should drop noticeably as the auto-pan sweeps toward hard right (start={startLeft}, atPeak={peakLeftSample})");
    }

    [Fact]
    public void StaticPan_WithAutoPanOff_MatchesExistingBehavior()
    {
        var effect = new StereoPannerEffect();
        effect.Parameters.Set("pan", 1f); // hard right
        var provider = effect.Wrap(new ConstantStereoProvider());

        var buffer = new float[256];
        provider.Read(buffer, 0, buffer.Length);

        // Hard right: left channel should be near-silent, right near full.
        Assert.InRange(buffer[0], -0.05f, 0.05f);
        Assert.InRange(buffer[1], 0.45f, 0.55f);
    }

    [Fact]
    public void ReadWritePoints_RoundTrips_WithoutReordering()
    {
        var effect = new StereoPannerEffect();
        PanPoint[] points = [new(0.6f, -0.2f), new(0.1f, 0.9f), new(0.9f, 0.1f)];

        effect.WritePoints(points);
        var read = effect.ReadPoints();

        Assert.Equal(points.Length, read.Count);
        for (var i = 0; i < points.Length; i++)
        {
            Assert.Equal(points[i].Time, read[i].Time, 3);
            Assert.Equal(points[i].Pan, read[i].Pan, 3);
        }
    }
}
