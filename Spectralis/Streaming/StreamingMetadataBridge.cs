using System;
using Spectralis.Library;

namespace Spectralis.Streaming
{
    public static class StreamingMetadataBridge
    {
        public static TrackInfo ToTrackInfo(StreamingTrack track)
        {
            return new TrackInfo
            {
                Title = track.Title ?? "Unknown Title",
                Artist = track.Artist ?? "Unknown Artist",
                Album = track.Album ?? string.Empty,
                Duration = track.Duration,
                FilePath = track.StreamUrl ?? string.Empty,
                IsStreamed = true,
                StreamSource = track.Source
            };
        }

        public static StreamingTrack FromTrackInfo(TrackInfo info)
        {
            return new StreamingTrack
            {
                Title = info.Title,
                Artist = info.Artist,
                Album = info.Album,
                Duration = info.Duration,
                StreamUrl = info.FilePath,
                Source = info.StreamSource ?? "Unknown"
            };
        }
    }
}
