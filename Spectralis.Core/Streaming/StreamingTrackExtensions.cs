using Spectralis.Core.Audio;

namespace Spectralis.Core.Streaming
{
    public static class StreamingTrackExtensions
    {
        public static TrackInfo ToTrackInfo(this StreamingTrack track) => new()
        {
            Title = track.Title,
            Artist = track.Artist,
            Album = track.Album,
            FilePath = $"streaming://{track.SourceId}/{track.Id}",
            DurationSeconds = track.DurationSeconds
        };
    }
}
