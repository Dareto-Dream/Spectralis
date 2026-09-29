using NAudio.Wave;
using Spectralis.Core.Audio.Effects;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class EffectChainPresetsTests
{
    private sealed class SineProvider : ISampleProvider
    {
        private int _sample;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i += 2)
            {
                var value = (float)Math.Sin(2 * Math.PI * 440 * _sample / 44100.0) * 0.8f;
                buffer[offset + i] = value;
                buffer[offset + i + 1] = value;
                _sample++;
            }

            return count;
        }
    }

    [Fact]
    public void Default_IsJustOneFlatParametricEq()
    {
        var effects = EffectChainPresets.Build(EffectChainPresets.DefaultName);

        var eq = Assert.Single(effects);
        var parametricEq = Assert.IsType<ParametricEqEffect>(eq);
        Assert.All(parametricEq.ReadBands(), band => Assert.Equal(0f, band.GainDb));
    }

    [Fact]
    public void MakeMusicPeak_IncludesBassBoostedEqAndAutoPanningPanner()
    {
        var effects = EffectChainPresets.Build(EffectChainPresets.MakeMusicPeakName);

        var eq = Assert.IsType<ParametricEqEffect>(effects[0]);
        Assert.True(eq.ReadBands()[0].GainDb > 0, "Make Music Peak should bass-boost the low end");

        var panner = Assert.IsType<StereoPannerEffect>(effects.OfType<StereoPannerEffect>().Single());
        Assert.True(panner.AutoPanEnabled);
    }

    [Theory]
    [InlineData(EffectChainPresets.DefaultName)]
    [InlineData(EffectChainPresets.MakeMusicPeakName)]
    public void BuiltInPresets_ProcessAudioWithoutThrowing(string name)
    {
        var chain = new EffectChain();
        chain.ReplaceAll(EffectChainPresets.Build(name));

        var provider = chain.BuildChain(new SineProvider());
        var buffer = new float[8192];
        var read = provider.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);
        Assert.All(buffer, sample => Assert.InRange(sample, -1f, 1f));
    }

    [Fact]
    public void Build_UnknownName_Throws()
    {
        Assert.Throws<ArgumentException>(() => EffectChainPresets.Build("Not A Real Preset"));
    }
}
