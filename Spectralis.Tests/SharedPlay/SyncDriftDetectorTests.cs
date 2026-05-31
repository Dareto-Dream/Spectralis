using System;
using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.SharedPlay
{
    public class SyncDriftDetectorTests
    {
        [Fact]
        public void IsDrifting_FalseWhenSynced()
        {
            var detector = new SyncDriftDetector();
            for (int i = 0; i < 5; i++)
                detector.Record(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10.1));
            Assert.False(detector.IsDrifting);
        }

        [Fact]
        public void IsDrifting_TrueWhenLargeDrift()
        {
            var detector = new SyncDriftDetector();
            for (int i = 0; i < 5; i++)
                detector.Record(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12));
            Assert.True(detector.IsDrifting);
        }

        [Fact]
        public void Reset_ClearsState()
        {
            var detector = new SyncDriftDetector();
            for (int i = 0; i < 5; i++)
                detector.Record(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(5));
            detector.Reset();
            Assert.False(detector.IsDrifting);
            Assert.Equal(0, detector.AverageDriftSeconds);
        }

        [Fact]
        public void AverageDrift_ComputesCorrectly()
        {
            var detector = new SyncDriftDetector();
            detector.Record(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10.2));
            detector.Record(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10.4));
            Assert.True(detector.AverageDriftSeconds > 0);
        }
    }
}
