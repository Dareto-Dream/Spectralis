using FluentAssertions;
using Spectralis.Core.Audio;
using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.Visualizers
{
    public class AudioFrameAdapterTests
    {
        private static float[] MakeSpectrum(int length, float value = 1f)
        {
            var s = new float[length];
            for (int i = 0; i < length; i++) s[i] = value;
            return s;
        }

        [Fact]
        public void GetBandEnergy_AveragesCorrectBand()
        {
            var spectrum = new float[64];
            for (int i = 0; i < 16; i++) spectrum[i + 16] = 0.5f;
            var frame = new AudioFrame { Spectrum = spectrum, Waveform = new float[512] };

            float energy = AudioFrameAdapter.GetBandEnergy(frame, 1, 4);
            energy.Should().BeApproximately(0.5f, 0.01f);
        }

        [Fact]
        public void GetBandEnergy_EmptySpectrum_ReturnsZero()
        {
            var frame = new AudioFrame { Spectrum = new float[0], Waveform = new float[0] };
            AudioFrameAdapter.GetBandEnergy(frame, 0, 4).Should().Be(0f);
        }

        [Fact]
        public void GetLoudness_AveragesLeftRight()
        {
            var frame = new AudioFrame
            {
                Spectrum = new float[64],
                Waveform = new float[512],
                RmsLeft = 0.4f,
                RmsRight = 0.8f
            };
            AudioFrameAdapter.GetLoudness(frame).Should().BeApproximately(0.6f, 0.001f);
        }

        [Fact]
        public void GetPeak_ReturnsMajorChannel()
        {
            var frame = new AudioFrame
            {
                Spectrum = new float[64],
                Waveform = new float[512],
                PeakLeft = 0.3f,
                PeakRight = 0.9f
            };
            AudioFrameAdapter.GetPeak(frame).Should().BeApproximately(0.9f, 0.001f);
        }

        [Fact]
        public void SubsampleSpectrum_ReducesBands()
        {
            float[] full = MakeSpectrum(64, 0.5f);
            float[] sub = AudioFrameAdapter.SubsampleSpectrum(full, 8);
            sub.Should().HaveCount(8);
            sub.Should().AllSatisfy(v => v.Should().BeApproximately(0.5f, 0.001f));
        }

        [Fact]
        public void SubsampleSpectrum_EmptyInput_ReturnsZeroArray()
        {
            float[] result = AudioFrameAdapter.SubsampleSpectrum(new float[0], 4);
            result.Should().HaveCount(4);
            result.Should().AllSatisfy(v => v.Should().Be(0f));
        }

        [Fact]
        public void SubsampleSpectrum_MoreTargetThanSource_HandledGracefully()
        {
            float[] result = AudioFrameAdapter.SubsampleSpectrum(new float[4] { 1f, 2f, 3f, 4f }, 8);
            result.Should().HaveCount(8);
        }
    }
}
