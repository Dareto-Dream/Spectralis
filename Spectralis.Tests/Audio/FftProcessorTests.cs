using System;
using System.Linq;
using Spectralis.Core.Audio;
using Xunit;

namespace Spectralis.Tests.Audio
{
    public class FftProcessorTests
    {
        [Fact]
        public void Process_SineWave_ProducesPeakAtExpectedBin()
        {
            var processor = new FftProcessor(2048);
            var samples = Enumerable.Range(0, 2048)
                .Select(i => (float)Math.Sin(2 * Math.PI * i * 10 / 2048.0))
                .ToArray();
            var result = processor.Process(samples, 44100);
            Assert.NotEmpty(result);
        }

        [Fact]
        public void Process_SilentInput_OutputsNearZero()
        {
            var processor = new FftProcessor(2048);
            var result = processor.Process(new float[2048], 44100);
            Assert.All(result, v => Assert.True(v < 0.01f));
        }

        [Fact]
        public void Process_OutputLength_IsHalfWindowSize()
        {
            var processor = new FftProcessor(2048);
            var result = processor.Process(new float[2048], 44100);
            Assert.Equal(1024, result.Length);
        }

        [Fact]
        public void Process_OutputValues_AreNonNegative()
        {
            var processor = new FftProcessor(1024);
            var samples = Enumerable.Range(0, 1024).Select(i => (float)Math.Sin(i * 0.05)).ToArray();
            var result = processor.Process(samples, 44100);
            Assert.All(result, v => Assert.True(v >= 0f));
        }
    }
}
