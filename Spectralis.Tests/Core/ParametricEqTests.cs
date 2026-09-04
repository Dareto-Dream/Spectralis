using NAudio.Wave;
using Spectralis.Core.Audio.Effects;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class ParametricEqTests
{
    private sealed class SineProvider(double frequencyHz, double amplitude = 0.1) : ISampleProvider
    {
        private int _n;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i += 2)
            {
                var v = (float)(Math.Sin(2 * Math.PI * frequencyHz * _n / 48000.0) * amplitude);
                buffer[offset + i] = v;
                buffer[offset + i + 1] = v;
                _n++;
            }

            return count;
        }
    }

    private static double Rms(ISampleProvider provider, int frames = 48000)
    {
        var buffer = new float[frames * 2];
        // Prime the filter state, then measure.
        provider.Read(buffer, 0, buffer.Length);
        provider.Read(buffer, 0, buffer.Length);
        double sum = 0;
        foreach (var s in buffer)
        {
            sum += s * (double)s;
        }

        return Math.Sqrt(sum / buffer.Length);
    }

    [Fact]
    public void Default_IsFlat()
    {
        var eq = new ParametricEqEffect();
        var dry = Rms(new SineProvider(1000));
        var wet = Rms(eq.Wrap(new SineProvider(1000)));
        Assert.Equal(dry, wet, 2);
    }

    [Fact]
    public void PeakBand_BoostsEnergyAtItsCentreFrequency()
    {
        var eq = new ParametricEqEffect();
        var bands = eq.ReadBands().ToList();
        var idx = bands.FindIndex(b => Math.Abs(b.Frequency - 1000) < 1);
        bands[idx] = bands[idx] with { GainDb = 12f, Q = 2f };
        eq.WriteBands(bands);

        var dry = Rms(new SineProvider(1000));
        var wet = Rms(eq.Wrap(new SineProvider(1000)));

        var gainDb = 20 * Math.Log10(wet / dry);
        Assert.InRange(gainDb, 9, 13);
    }

    [Fact]
    public void ParamChange_BumpsRevision_AndTakesEffectWithoutReWrapping()
    {
        var eq = new ParametricEqEffect();
        var provider = eq.Wrap(new SineProvider(4000));
        var before = Rms(provider);

        var rev = eq.Parameters.Revision;
        var bands = eq.ReadBands().ToList();
        var idx = bands.FindIndex(b => Math.Abs(b.Frequency - 4000) < 1);
        bands[idx] = bands[idx] with { GainDb = -18f, Q = 3f };
        eq.WriteBands(bands);

        Assert.True(eq.Parameters.Revision > rev);

        var after = Rms(provider);
        Assert.True(after < before * 0.7, $"expected a cut: before={before}, after={after}");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(16)]
    public void ArbitraryBandCount_ProcessesWithoutThrowing(int count)
    {
        var eq = new ParametricEqEffect();
        var bands = Enumerable.Range(0, count)
            .Select(i => new EqBand(100f * (i + 1), (i % 2 == 0) ? 4f : -4f, 1.2f, EqFilterType.Peak))
            .ToList();
        eq.WriteBands(bands);
        Assert.Equal(count, eq.BandCount);

        var provider = eq.Wrap(new SineProvider(1000));
        var buffer = new float[8192];
        var read = provider.Read(buffer, 0, buffer.Length);
        Assert.Equal(buffer.Length, read);
        Assert.All(buffer, s => Assert.InRange(s, -1f, 1f));
    }

    [Fact]
    public void Biquad_PeakMagnitude_MatchesGainAtCentre()
    {
        var db = Biquad.MagnitudeDb(EqFilterType.Peak, 48000, 1000, 1.0, 6.0, 1000);
        Assert.Equal(6.0, db, 1);
    }

    [Fact]
    public void Biquad_HighPass_AttenuatesBelowCutoff()
    {
        var below = Biquad.MagnitudeDb(EqFilterType.HighPass, 48000, 1000, 0.707, 0, 100);
        var above = Biquad.MagnitudeDb(EqFilterType.HighPass, 48000, 1000, 0.707, 0, 8000);
        Assert.True(below < -20);
        Assert.InRange(above, -1, 0.5);
    }

    [Fact]
    public void BuiltInFlatPreset_IsTrulyFlat()
    {
        var eq = new ParametricEqEffect();
        EqPresets.Flat.ApplyTo(eq);
        for (var hz = 30.0; hz < 16000; hz *= 1.5)
        {
            Assert.InRange(eq.ResponseDb(hz), -0.05, 0.05);
        }
    }

    [Fact]
    public void Preset_RoundTripsThroughFromEffect()
    {
        var eq = new ParametricEqEffect { PreampDb = -3f };
        EqPresets.BuiltIn.First(p => p.Name == "Bass Boost").ApplyTo(eq);
        var captured = EqPreset.FromEffect(eq, "snapshot");

        var eq2 = new ParametricEqEffect();
        captured.ApplyTo(eq2);

        Assert.Equal(eq.ReadBands().Count, eq2.ReadBands().Count);
        for (var i = 0; i < eq.ReadBands().Count; i++)
        {
            Assert.Equal(eq.ReadBands()[i].GainDb, eq2.ReadBands()[i].GainDb, 3);
            Assert.Equal(eq.ReadBands()[i].Frequency, eq2.ReadBands()[i].Frequency, 3);
        }
    }
}
