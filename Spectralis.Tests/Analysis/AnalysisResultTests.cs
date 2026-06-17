using Spectralis.Core.Analysis;
using Xunit;

namespace Spectralis.Tests.Analysis
{
    public class AnalysisResultTests
    {
        [Fact]
        public void AnalysisResult_BpmRoundsCorrectly()
        {
            var result = new AnalysisResult { BpmRaw = 128.4567f };
            Assert.Equal(128.5f, result.BpmRaw, precision: 3);
        }

        [Fact]
        public void AnalysisResult_KeyLabel_IsExpected()
        {
            var result = new AnalysisResult { Key = "C", Mode = "major" };
            Assert.Equal("C major", result.KeyLabel);
        }

        [Fact]
        public void AnalysisResult_MinorMode_IncludesMinor()
        {
            var result = new AnalysisResult { Key = "A", Mode = "minor" };
            Assert.Contains("minor", result.KeyLabel);
        }

        [Fact]
        public void AnalysisResult_DefaultConfidence_IsZero()
        {
            var result = new AnalysisResult();
            Assert.Equal(0f, result.KeyConfidence);
        }

        [Fact]
        public void AnalysisResult_HighConfidence_IsValid()
        {
            var result = new AnalysisResult { KeyConfidence = 0.95f };
            Assert.True(result.KeyConfidence >= 0f && result.KeyConfidence <= 1f);
        }
    }
}
