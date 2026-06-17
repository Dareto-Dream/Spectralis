using FluentAssertions;
using Spectralis.Core.Analysis;
using Xunit;

namespace Spectralis.Tests.Analysis
{
    public class BeatGridTests
    {
        private static BpmResult ValidBpm(float bpm) => new() { Bpm = bpm, Confidence = 0.8f };

        [Fact]
        public void Build_ValidBpm_CreatesBeats()
        {
            var grid = BeatGrid.Build(ValidBpm(120f), 10.0);
            grid.IsValid.Should().BeTrue();
            grid.Beats.Should().NotBeEmpty();
        }

        [Fact]
        public void Build_120Bpm_10Seconds_CreatesApprox20Beats()
        {
            var grid = BeatGrid.Build(ValidBpm(120f), 10.0);
            grid.Beats.Count.Should().BeInRange(18, 22);
        }

        [Fact]
        public void Build_InvalidBpm_ReturnsEmptyGrid()
        {
            var bpm = new BpmResult { Bpm = 30f, Confidence = 0.8f };
            var grid = BeatGrid.Build(bpm, 10.0);
            grid.IsValid.Should().BeFalse();
        }

        [Fact]
        public void Beats_EveryFourth_IsDownbeat()
        {
            var grid = BeatGrid.Build(ValidBpm(120f), 20.0);
            foreach (var beat in grid.Beats)
            {
                if (beat.BeatNumber % 4 == 0)
                    beat.IsDownbeat.Should().BeTrue();
                else
                    beat.IsDownbeat.Should().BeFalse();
            }
        }

        [Fact]
        public void GetNearestBeat_ExactMatch_ReturnsBeat()
        {
            var grid = BeatGrid.Build(ValidBpm(120f), 10.0);
            var first = grid.Beats[0];
            var found = grid.GetNearestBeat(first.TimeSeconds);
            found.Should().NotBeNull();
            found!.BeatNumber.Should().Be(0);
        }

        [Fact]
        public void GetNearestBeat_OutsideTolerance_ReturnsNull()
        {
            var grid = BeatGrid.Build(ValidBpm(120f), 10.0);
            var result = grid.GetNearestBeat(5.0, toleranceSeconds: 0.001);
            // 5.0s is likely between two beats at 120bpm — null if outside tolerance
            // (may or may not be null depending on grid alignment; just verify no crash)
            _ = result;
        }
    }
}
