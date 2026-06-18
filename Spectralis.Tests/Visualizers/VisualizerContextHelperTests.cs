using FluentAssertions;
using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.Visualizers
{
    public class VisualizerContextHelperTests
    {
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(0.01f, 0f)]
        [InlineData(0.02f, 0f)]
        [InlineData(0.5f, 0.565f)]
        [InlineData(0.85f, 1f)]
        [InlineData(1.5f, 1f)]
        public void NormalizeEnergy_ClampsAndScales(float input, float expected)
        {
            float result = VisualizerContextHelper.NormalizeEnergy(input);
            result.Should().BeApproximately(expected, 0.01f);
        }

        [Fact]
        public void SmoothToward_RateZero_ReturnsCurrent()
        {
            float result = VisualizerContextHelper.SmoothToward(0.2f, 0.8f, 0f);
            result.Should().BeApproximately(0.2f, 0.001f);
        }

        [Fact]
        public void SmoothToward_RateOne_ReturnsTarget()
        {
            float result = VisualizerContextHelper.SmoothToward(0.2f, 0.8f, 1f);
            result.Should().BeApproximately(0.8f, 0.001f);
        }

        [Fact]
        public void ComputeBandEnergies_EvenSpread_AveragesCorrectly()
        {
            float[] spectrum = new float[8];
            for (int i = 0; i < 8; i++) spectrum[i] = i * 0.1f;
            float[] bands = VisualizerContextHelper.ComputeBandEnergies(spectrum, 4);
            bands.Should().HaveCount(4);
            bands[0].Should().BeApproximately(0.05f, 0.001f);
            bands[3].Should().BeApproximately(0.65f, 0.001f);
        }

        [Fact]
        public void BassEnergy_UsesFirstEighth()
        {
            float[] spectrum = new float[64];
            for (int i = 0; i < 8; i++) spectrum[i] = 1f;
            float bass = VisualizerContextHelper.BassEnergy(spectrum);
            bass.Should().BeApproximately(1f, 0.001f);
        }

        [Fact]
        public void HighEnergy_UsesUpperHalf()
        {
            float[] spectrum = new float[64];
            for (int i = 32; i < 64; i++) spectrum[i] = 0.8f;
            float high = VisualizerContextHelper.HighEnergy(spectrum);
            high.Should().BeApproximately(0.8f, 0.001f);
        }

        [Fact]
        public void EmptySpectrum_AllMethodsReturnZero()
        {
            float[] empty = new float[0];
            VisualizerContextHelper.BassEnergy(empty).Should().Be(0f);
            VisualizerContextHelper.MidEnergy(empty).Should().Be(0f);
            VisualizerContextHelper.HighEnergy(empty).Should().Be(0f);
            VisualizerContextHelper.ComputeBandEnergies(empty, 4).Should().AllBeEquivalentTo(0f);
        }
    }
}
