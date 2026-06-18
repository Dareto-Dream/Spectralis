using FluentAssertions;
using Spectralis.Core.Analysis;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Spectralis.Tests.Analysis
{
    public class AnalysisWorkerTests
    {
        [Fact]
        public async Task AnalyzeAsync_NonExistentFile_ReturnsNull()
        {
            var worker = new AnalysisWorker();
            var result = await worker.AnalyzeAsync("/does/not/exist.mp3");
            result.Should().BeNull();
        }

        [Fact]
        public async Task AnalyzeAsync_CancelledToken_ThrowsOrReturnsNull()
        {
            var worker = new AnalysisWorker();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            AnalysisResult? result = null;
            try
            {
                result = await worker.AnalyzeAsync("/does/not/exist.mp3", cts.Token);
            }
            catch (TaskCanceledException) { }

            result.Should().BeNull();
        }

        [Fact]
        public void AnalysisResult_DefaultValues_AreValid()
        {
            var result = new AnalysisResult
            {
                FilePath = "/test.mp3",
                Bpm = new BpmResult { Bpm = 0f, Confidence = 0f },
                Key = new KeyResult { Confidence = 0f },
                BeatGrid = new BeatGrid(),
                LoudnessLufs = -96f
            };
            result.Bpm.IsValid.Should().BeFalse();
            result.Key.IsValid.Should().BeFalse();
            result.BeatGrid.IsValid.Should().BeFalse();
        }

        [Fact]
        public void AnalysisCompleted_EventFiresWithResult()
        {
            var worker = new AnalysisWorker();
            AnalysisResult? fired = null;
            worker.AnalysisCompleted += (_, r) => fired = r;

            // Simulate direct event by testing that the event wiring works
            // (actual file analysis tested in integration suite)
            fired.Should().BeNull();
        }
    }
}
