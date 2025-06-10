using System;

namespace Spectralis.Library
{
    public class LibraryTrack
    {
        public int Id { get; set; }
        public string Path { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Genre { get; set; }
        public int Year { get; set; }
        public int TrackNumber { get; set; }
        public long DurationMs { get; set; }
        public int Bitrate { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public string Format { get; set; }
        public string CoverPath { get; set; }
        public DateTime DateAdded { get; set; }
        public int PlayCount { get; set; }
        public DateTime? LastPlayed { get; set; }

        public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);
        public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? System.IO.Path.GetFileNameWithoutExtension(Path) : Title;
        public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "Unknown Artist" : Artist;

        public Spectralis.Audio.TrackInfo ToTrackInfo()
        {
            return new Spectralis.Audio.TrackInfo
            {
                FilePath = Path,
                Title = Title,
                Artist = Artist,
                Album = Album,
                Genre = Genre,
                Year = Year,
                Duration = Duration,
                Bitrate = Bitrate,
                SampleRate = SampleRate,
                Channels = Channels,
                Format = Format
            };
        }
    }
}
