using System;

namespace Spectralis.Audio
{
    public class TrackChangedEventArgs : EventArgs
    {
        public TrackInfo PreviousTrack { get; }
        public TrackInfo NewTrack { get; }

        public TrackChangedEventArgs(TrackInfo previous, TrackInfo next)
        {
            PreviousTrack = previous;
            NewTrack = next;
        }
    }

    public class PlaybackErrorEventArgs : EventArgs
    {
        public Exception Exception { get; }
        public string FilePath { get; }
        public bool Handled { get; set; }

        public PlaybackErrorEventArgs(Exception ex, string filePath)
        {
            Exception = ex;
            FilePath = filePath;
        }
    }
}
