using Spectralis.Core.Effects;
using Xunit;

namespace Spectralis.Tests.Effects
{
    public class EqualizerEffectTests
    {
        [Fact]
        public void DefaultGain_IsZeroDb()
        {
            var eq = new EqualizerEffect();
            foreach (var band in eq.Bands)
                Assert.Equal(0f, band.GainDb);
        }

        [Fact]
        public void SetBandGain_PersistsValue()
        {
            var eq = new EqualizerEffect();
            eq.Bands[2].GainDb = 6.0f;
            Assert.Equal(6.0f, eq.Bands[2].GainDb);
        }

        [Fact]
        public void BandCount_IsExpected()
        {
            var eq = new EqualizerEffect();
            Assert.Equal(10, eq.Bands.Count);
        }

        [Fact]
        public void Reset_SetsAllGainsToZero()
        {
            var eq = new EqualizerEffect();
            eq.Bands[0].GainDb = 3f;
            eq.Bands[5].GainDb = -3f;
            eq.Reset();
            Assert.All(eq.Bands, b => Assert.Equal(0f, b.GainDb));
        }

        [Fact]
        public void IsEnabled_DefaultTrue()
        {
            var eq = new EqualizerEffect();
            Assert.True(eq.IsEnabled);
        }
    }
}
