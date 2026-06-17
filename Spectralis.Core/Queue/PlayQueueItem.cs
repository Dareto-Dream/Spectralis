using System;
using Spectralis.Core.Models;

namespace Spectralis.Core.Queue
{
    public class PlayQueueItem
    {
        public Guid Id { get; } = Guid.NewGuid();
        public TrackInfo Track { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        public PlayQueueItem(TrackInfo track) => Track = track;

        public override string ToString() =>
            Track != null ? $"{Track.Artist} — {Track.Title}" : "(empty)";
    }
}
