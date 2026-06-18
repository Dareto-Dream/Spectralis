using System.Linq;
using Spectralis.Core.Audio;
using Xunit;

namespace Spectralis.Tests.Audio
{
    public class SpectrumSmootherTests
    {
        [Fact]
        public void Smooth_WithSameLengths_ReturnsCorrectLength()
        {
            var smoother = new SpectrumSmoother(64, 0.8f);
            var prev = Enumerable.Repeat(0.5f, 64).ToArray();
            var curr = Enumerable.Repeat(1.0f, 64).ToArray();
            var result = smoother.Smooth(prev, curr);
            Assert.Equal(64, result.Length);
        }

        [Fact]
        public void Smooth_Factor1_ReturnsPreviousValues()
        {
            var smoother = new SpectrumSmoother(4, 1.0f);
            var prev = new float[] { 1, 2, 3, 4 };
            var curr = new float[] { 0, 0, 0, 0 };
            var result = smoother.Smooth(prev, curr);
            Assert.Equal(prev, result);
        }

        [Fact]
        public void Smooth_Factor0_ReturnsCurrentValues()
        {
            var smoother = new SpectrumSmoother(4, 0.0f);
            var prev = new float[] { 10, 20, 30, 40 };
            var curr = new float[] { 1, 2, 3, 4 };
            var result = smoother.Smooth(prev, curr);
            Assert.Equal(curr, result);
        }

        [Fact]
        public void Smooth_MidFactor_InterpolatesValues()
        {
            var smoother = new SpectrumSmoother(1, 0.5f);
            var prev = new float[] { 0.0f };
            var curr = new float[] { 1.0f };
            var result = smoother.Smooth(prev, curr);
            Assert.Equal(0.5f, result[0], precision: 5);
        }
    }
}
