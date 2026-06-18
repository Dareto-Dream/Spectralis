using FluentAssertions;
using Spectralis.Core.Analysis;
using Xunit;

namespace Spectralis.Tests.Analysis
{
    public class KeyAnalyzerTests
    {
        private static float[] CMajorChromagram()
        {
            var c = new float[12];
            c[0] = 6.35f; c[2] = 3.48f; c[4] = 4.38f; c[5] = 4.09f;
            c[7] = 5.19f; c[9] = 3.66f; c[11] = 2.88f;
            return c;
        }

        private static float[] AMinorChromagram()
        {
            var c = new float[12];
            c[9] = 6.33f; c[11] = 3.52f; c[0] = 2.60f; c[2] = 4.75f;
            c[4] = 3.98f; c[5] = 3.34f; c[7] = 2.54f;
            return c;
        }

        [Fact]
        public void Analyze_CMajorChromagram_DetectsCMajor()
        {
            var analyzer = new KeyAnalyzer();
            var result = analyzer.Analyze(CMajorChromagram());
            result.IsMajor.Should().BeTrue();
            result.Name.Should().Contain("C");
        }

        [Fact]
        public void Analyze_AMinorChromagram_DetectsMinor()
        {
            var analyzer = new KeyAnalyzer();
            var result = analyzer.Analyze(AMinorChromagram());
            result.IsMajor.Should().BeFalse();
        }

        [Fact]
        public void Analyze_AllZeros_ReturnsLowConfidence()
        {
            var analyzer = new KeyAnalyzer();
            var result = analyzer.Analyze(new float[12]);
            result.IsValid.Should().BeFalse();
        }

        [Fact]
        public void Analyze_ShortInput_DoesNotThrow()
        {
            var analyzer = new KeyAnalyzer();
            var act = () => analyzer.Analyze(new float[4]);
            act.Should().NotThrow();
        }

        [Fact]
        public void KeyResult_IsValid_RequiresMinConfidence()
        {
            var r = new KeyResult { Confidence = 0.2f };
            r.IsValid.Should().BeFalse();
        }

        [Fact]
        public void Analyze_ConfidenceRange_ZeroToOne()
        {
            var analyzer = new KeyAnalyzer();
            var result = analyzer.Analyze(CMajorChromagram());
            result.Confidence.Should().BeInRange(0f, 1f);
        }
    }
}
