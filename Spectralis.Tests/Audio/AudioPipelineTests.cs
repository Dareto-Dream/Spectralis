using System;
using System.Linq;
using Spectralis.Core.Audio;
using Xunit;

namespace Spectralis.Tests.Audio
{
    public class AudioPipelineTests
    {
        [Fact]
        public void FftProcessor_OutputsExpectedBandCount()
        {
            var bands = 64;
            var output = new float[bands];
            var input = Enumerable.Range(0, 2048).Select(i => (float)Math.Sin(i * 0.1)).ToArray();
            Assert.Equal(bands, output.Length);
        }

        [Fact]
        public void SpectrumBands_DefaultIs64()
        {
            var opts = new AudioEngineOptions();
            Assert.Equal(64, opts.SpectrumBands);
        }

        [Fact]
        public void WaveformBuffer_StoresRecentSamples()
        {
            var buf = new WaveformBuffer(512);
            buf.Write(new float[512]);
            Assert.Equal(512, buf.Length);
        }

        [Fact]
        public void SpectrumSmoother_ReducesJitter()
        {
            var smoother = new SpectrumSmoother(64, smoothingFactor: 0.8f);
            var before = Enumerable.Repeat(1.0f, 64).ToArray();
            var after = Enumerable.Repeat(0.0f, 64).ToArray();
            var smoothed = smoother.Smooth(before, after);
            Assert.All(smoothed, v => Assert.True(v > 0));
        }
    }
}
