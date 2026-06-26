using NAudio.Wave;
using Spectralis.Core.Audio.Effects;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class EffectChainTests
{
    private sealed class SilenceProvider : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
    }

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
    public void BuildChain_Disabled_ReturnsSourceUnchanged()
    {
        var chain = new EffectChain { Enabled = false };
        chain.Add(new Eq10BandEffect());
        var source = new SilenceProvider();

        Assert.Same(source, chain.BuildChain(source));
    }

    [Fact]
    public void BuildChain_WrapsOnlyEnabledEffects()
    {
        var chain = new EffectChain();
        var eq = new Eq10BandEffect { Enabled = false };
        chain.Add(eq);
        var source = new SilenceProvider();

        Assert.Same(source, chain.BuildChain(source));

        eq.Enabled = true;
        Assert.NotSame(source, chain.BuildChain(source));
    }

    [Fact]
    public void Chain_RaisesChangedOnMutations()
    {
        var chain = new EffectChain();
        var changes = 0;
        chain.Changed += (_, _) => changes++;

        var eq = new Eq10BandEffect();
        chain.Add(eq);
        chain.Add(new ReverbEffect());
        chain.MoveUp(1);
        chain.MoveDown(0);
        chain.Remove(eq);
        chain.NotifyChanged();

        Assert.Equal(6, changes);
    }

    [Fact]
    public void AllEffects_ProcessAudioWithoutThrowing()
    {
        foreach (var name in EffectChain.AvailableEffects)
        {
            var chain = new EffectChain();
            chain.Add(EffectChain.CreateEffect(name));

            var provider = chain.BuildChain(new SineProvider());
            var buffer = new float[4096];
            var read = provider.Read(buffer, 0, buffer.Length);

            Assert.Equal(buffer.Length, read);
            Assert.All(buffer, sample => Assert.InRange(sample, -1f, 1f));
        }
    }

    [Fact]
    public void VocalBlend_FullBlend_CancelsCenteredContent()
    {
        var effect = new VocalBlendEffect();
        effect.Parameters.Set("blend", 1f);

        // A mono-centered signal (L == R) should cancel to silence at full blend.
        var provider = effect.Wrap(new SineProvider());
        var buffer = new float[4096];
        provider.Read(buffer, 0, buffer.Length);

        Assert.All(buffer, sample => Assert.InRange(Math.Abs(sample), 0f, 0.0001f));
    }

    [Fact]
    public void CreateEffect_UnknownName_Throws()
    {
        Assert.Throws<ArgumentException>(() => EffectChain.CreateEffect("Flanger"));
    }
}
