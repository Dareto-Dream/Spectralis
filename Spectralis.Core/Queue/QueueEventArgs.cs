using System;
using Spectralis.Core.Models;

namespace Spectralis.Core.Queue
{
    public class TrackChangedEventArgs : EventArgs
    {
        public TrackInfo? Track { get; }
        public int Index { get; }
        public TrackChangedEventArgs(TrackInfo? track, int index) { Track = track; Index = index; }
    }

    public class QueueChangedEventArgs : EventArgs
    {
        public QueueChangeReason Reason { get; }
        public int AffectedIndex { get; }
        public QueueChangedEventArgs(QueueChangeReason reason, int affectedIndex = -1)
        {
            Reason = reason;
            AffectedIndex = affectedIndex;
        }
    }

    public enum QueueChangeReason
    {
        Enqueued,
        Removed,
        Moved,
        Cleared,
        ShuffleToggled,
        RepeatChanged
    }
}
