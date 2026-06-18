using FluentAssertions;
using Spectralis.Core.Analysis;
using System;
using Xunit;

namespace Spectralis.Tests.Analysis
{
    public class BpmAnalyzerTests
    {
        private static float[] GenerateClickTrack(float bpm, int sampleRate = 44100, float durationSeconds = 10f)
        {
            int totalSamples = (int)(sampleRate * durationSeconds);
            var samples = new float[totalSamples];
            double beatInterval = 60.0 / bpm;
            int clickWidth = sampleRate / 100;

            for (double t = 0; t < durationSeconds; t += beatInterval)
            {
                int start = (int)(t * sampleRate);
                int end = Math.Min(start + clickWidth, totalSamples);
                for (int i = start; i < end; i++)
                    samples[i] = 0.9f;
            }
            return samples;
        }

        [Theory]
        [InlineData(120f)]
        [InlineData(140f)]
        [InlineData(90f)]
        public void Analyze_ClickTrack_DetectsApproximateBpm(float expectedBpm)
        {
            var analyzer = new BpmAnalyzer();
            float[] clicks = GenerateClickTrack(expectedBpm);

            var result = analyzer.Analyze(clicks);

            result.IsValid.Should().BeTrue();
            result.Bpm.Should().BeApproximately(expectedBpm, expectedBpm * 0.05f);
        }

        [Fact]
        public void Analyze_Silence_ReturnsInvalid()
        {
            var analyzer = new BpmAnalyzer();
            var result = analyzer.Analyze(new float[44100 * 5]);
            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public void Analyze_TooShort_ReturnsDefault()
        {
            var analyzer = new BpmAnalyzer();
            var result = analyzer.Analyze(new float[1000]);
            result.Bpm.Should().Be(0f);
        }

        [Fact]
        public void Analyze_Result_HasConfidenceInRange()
        {
            var analyzer = new BpmAnalyzer();
            float[] clicks = GenerateClickTrack(128f);
            var result = analyzer.Analyze(clicks);
            result.Confidence.Should().BeInRange(0f, 1f);
        }

        [Fact]
        public void BpmResult_IsValid_RequiresMinConfidence()
        {
            var result = new BpmResult { Bpm = 120f, Confidence = 0.3f };
            result.IsValid.Should().BeFalse();
        }
    }
}
