using CommunityToolkit.Mvvm.ComponentModel;
using Spectralis.Core.Streaming;

namespace Spectralis.App.ViewModels
{
    public class StreamingResultViewModel : ObservableObject
    {
        public StreamingTrack Track { get; }

        public string Title => Track.Title;
        public string Artist => Track.Artist;
        public string Album => Track.Album;
        public string Source => Track.SourceId;
        public string? ThumbnailUrl => Track.ThumbnailUrl;
        public string Duration => Track.DurationSeconds > 0
            ? $"{(int)(Track.DurationSeconds / 60)}:{(int)(Track.DurationSeconds % 60):D2}"
            : string.Empty;

        public StreamingResultViewModel(StreamingTrack track) => Track = track;
    }
}
