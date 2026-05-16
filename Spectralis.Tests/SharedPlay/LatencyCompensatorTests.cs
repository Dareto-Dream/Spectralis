using System;
using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.SharedPlay
{
    public class LatencyCompensatorTests
    {
        [Fact]
        public void Compensate_AddsNetworkDelay()
        {
            var comp = new LatencyCompensator(TimeSpan.FromMilliseconds(100));
            var packet = new SharedPlaySyncPacket
            {
                PositionSeconds = 10.0,
                ServerTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 50
            };

            var adjusted = comp.Compensate(packet);
            Assert.True(adjusted.TotalSeconds >= 10.0);
        }

        [Fact]
        public void Compensate_NeverGoesNegative()
        {
            var comp = new LatencyCompensator(TimeSpan.FromMilliseconds(50));
            var packet = new SharedPlaySyncPacket
            {
                PositionSeconds = 0,
                ServerTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5000
            };

            var adjusted = comp.Compensate(packet);
            Assert.True(adjusted >= TimeSpan.Zero);
        }

        [Fact]
        public void AdjustedPosition_AddsElapsedTime()
        {
            var comp = new LatencyCompensator(TimeSpan.FromMilliseconds(100));
            var remotePos = TimeSpan.FromSeconds(30);
            var remoteTime = DateTimeOffset.UtcNow.AddMilliseconds(-500);

            var adjusted = comp.AdjustedPosition(remotePos, remoteTime);
            Assert.True(adjusted > remotePos);
        }
    }
}
