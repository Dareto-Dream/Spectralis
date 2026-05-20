using System;
using System.Collections.Generic;

namespace Spectralis.Core.SharedPlay
{
    public class SharedPlayStats
    {
        public string SessionId { get; set; } = string.Empty;
        public string RoomCode { get; set; } = string.Empty;
        public int PeakListeners { get; set; }
        public int TotalJoins { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public List<string> TracksPlayed { get; set; } = new();
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }

        public void RecordJoin() => TotalJoins++;
        public void UpdatePeak(int current) { if (current > PeakListeners) PeakListeners = current; }
        public void RecordTrack(string trackId) { if (!TracksPlayed.Contains(trackId)) TracksPlayed.Add(trackId); }
    }
}
