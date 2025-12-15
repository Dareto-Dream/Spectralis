using FluentAssertions;
using Spectralis.Core.Audio;
using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.Visualizers
{
    public class BeatDetectorTests
    {
        private static AudioFrame MakeFrame(float bassEnergy)
        {
            var spectrum = new float[64];
            for (int i = 0; i < 8; i++) spectrum[i] = bassEnergy;
            return new AudioFrame
            {
                Spectrum = spectrum,
                Waveform = new float[512],
                RmsLeft = bassEnergy,
                RmsRight = bassEnergy,
                PeakLeft = bassEnergy,
                PeakRight = bassEnergy
            };
        }

        [Fact]
        public void Process_LowEnergy_NoBeat()
        {
            var detector = new BeatDetector();
            for (int i = 0; i < 50; i++)
                detector.Process(MakeFrame(0.05f));
            detector.IsBeat.Should().BeFalse();
        }

        [Fact]
        public void Process_SuddenHighEnergy_TriggersBeat()
        {
            var detector = new BeatDetector();
            for (int i = 0; i < 43; i++)
                detector.Process(MakeFrame(0.1f));

            detector.Process(MakeFrame(0.9f));
            detector.IsBeat.Should().BeTrue();
        }

        [Fact]
        public void Process_MinFrameGapPreventsDoubleTrigger()
        {
            var detector = new BeatDetector(minFrameGap: 8);
            for (int i = 0; i < 43; i++) detector.Process(MakeFrame(0.1f));
            detector.Process(MakeFrame(0.9f));

            int beatCount = 0;
            for (int i = 0; i < 5; i++)
            {
                detector.Process(MakeFrame(0.9f));
                if (detector.IsBeat) beatCount++;
            }
            beatCount.Should().Be(0);
        }

        [Fact]
        public void Sensitivity_HighValue_RequiresMoreEnergy()
        {
            var detector = new BeatDetector(sensitivity: 2.5f);
            for (int i = 0; i < 43; i++) detector.Process(MakeFrame(0.1f));
            detector.Process(MakeFrame(0.3f));
            detector.IsBeat.Should().BeFalse();
        }

        [Fact]
        public void BeatStrength_ReflectsEnergyRatio()
        {
            var detector = new BeatDetector();
            for (int i = 0; i < 43; i++) detector.Process(MakeFrame(0.1f));
            detector.Process(MakeFrame(0.9f));
            detector.BeatStrength.Should().BeGreaterThan(1f);
        }

        [Fact]
        public void EmptySpectrum_DoesNotThrow()
        {
            var detector = new BeatDetector();
            var frame = new AudioFrame { Spectrum = new float[0], Waveform = new float[0] };
            var act = () => detector.Process(frame);
            act.Should().NotThrow();
        }
    }
}
