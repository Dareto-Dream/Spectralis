using System.Linq;
using Spectralis.Core.Effects;
using Xunit;

namespace Spectralis.Tests.Effects
{
    public class EffectChainTests
    {
        [Fact]
        public void AddEffect_IncreasesCount()
        {
            var chain = new EffectChain();
            chain.Add(new EqualizerEffect());
            chain.Add(new CompressorEffect());
            Assert.Equal(2, chain.Effects.Count);
        }

        [Fact]
        public void RemoveEffect_DecreasesCount()
        {
            var chain = new EffectChain();
            var eq = new EqualizerEffect();
            chain.Add(eq);
            chain.Remove(eq);
            Assert.Empty(chain.Effects);
        }

        [Fact]
        public void EnabledEffects_OnlyReturnsEnabled()
        {
            var chain = new EffectChain();
            var eq = new EqualizerEffect { IsEnabled = true };
            var comp = new CompressorEffect { IsEnabled = false };
            chain.Add(eq);
            chain.Add(comp);
            var enabled = chain.Effects.Where(e => e.IsEnabled).ToList();
            Assert.Single(enabled);
        }

        [Fact]
        public void Clear_RemovesAllEffects()
        {
            var chain = new EffectChain();
            chain.Add(new EqualizerEffect());
            chain.Add(new CompressorEffect());
            chain.Clear();
            Assert.Empty(chain.Effects);
        }
    }
}
