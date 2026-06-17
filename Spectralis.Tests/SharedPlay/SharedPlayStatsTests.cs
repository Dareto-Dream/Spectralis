using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.SharedPlay
{
    public class SharedPlayStatsTests
    {
        [Fact]
        public void RecordJoin_IncrementsCount()
        {
            var stats = new SharedPlayStats();
            stats.RecordJoin();
            stats.RecordJoin();
            Assert.Equal(2, stats.TotalJoins);
        }

        [Fact]
        public void UpdatePeak_TracksPeak()
        {
            var stats = new SharedPlayStats();
            stats.UpdatePeak(3);
            stats.UpdatePeak(7);
            stats.UpdatePeak(2);
            Assert.Equal(7, stats.PeakListeners);
        }

        [Fact]
        public void RecordTrack_DeduplicatesIds()
        {
            var stats = new SharedPlayStats();
            stats.RecordTrack("t1");
            stats.RecordTrack("t1");
            stats.RecordTrack("t2");
            Assert.Equal(2, stats.TracksPlayed.Count);
        }
    }
}
