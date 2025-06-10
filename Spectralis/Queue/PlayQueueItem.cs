using System;
using Spectralis.Library;

namespace Spectralis.Queue
{
    public class PlayQueueItem
    {
        public Guid Id { get; } = Guid.NewGuid();
        public TrackInfo Track { get; set; }
        public bool IsStreamed { get; set; }
        public string StreamSource { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        public PlayQueueItem() { }

        public PlayQueueItem(TrackInfo track)
        {
            Track = track;
            IsStreamed = track.IsStreamed;
            StreamSource = track.StreamSource;
        }

        public override string ToString() =>
            Track != null ? $"{Track.Artist} — {Track.Title}" : "(empty)";
    }
}
