using System;

namespace Spectralis.Core.SharedPlay
{
    public class LatencyCompensator
    {
        private readonly TimeSpan _rtt;

        public LatencyCompensator(TimeSpan estimatedRoundTripTime)
        {
            _rtt = estimatedRoundTripTime;
        }

        public TimeSpan Compensate(SharedPlaySyncPacket packet)
        {
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long serverMs = packet.ServerTimestampMs;
            long travelMs = (nowMs - serverMs);
            double compensatedSeconds = packet.PositionSeconds + travelMs / 1000.0;
            return TimeSpan.FromSeconds(Math.Max(0, compensatedSeconds));
        }

        public TimeSpan AdjustedPosition(TimeSpan remotePosition, DateTimeOffset remoteTime)
        {
            var elapsed = DateTimeOffset.UtcNow - remoteTime - (_rtt / 2);
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            return remotePosition + elapsed;
        }
    }
}
