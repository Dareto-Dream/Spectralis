using System;
using System.Collections.Generic;

namespace Spectralis.Core.Timeline
{
    public class ReactiveTimeline
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public List<TimelineTrack> Tracks { get; } = new();

        public IEnumerable<TimelineEvent> GetEventsAt(TimeSpan position)
        {
            foreach (var track in Tracks)
                foreach (var evt in track.GetActiveEvents(position))
                    yield return evt;
        }
    }

    public class TimelineTrack
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public TimelineTrackType Type { get; set; }
        public List<TimelineEvent> Events { get; } = new();

        public IEnumerable<TimelineEvent> GetActiveEvents(TimeSpan position)
        {
            foreach (var evt in Events)
            {
                if (evt.StartTime <= position && position < evt.StartTime + evt.Duration)
                    yield return evt;
            }
        }
    }

    public class TimelineEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public TimeSpan StartTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string Type { get; set; } = string.Empty;
        public Dictionary<string, object?> Payload { get; set; } = new();

        public TimeSpan EndTime => StartTime + Duration;
        public bool Contains(TimeSpan position) => position >= StartTime && position < EndTime;
    }

    public enum TimelineTrackType
    {
        Visual,
        Audio,
        Lyrics,
        Metadata,
        Custom
    }
}
