using System;
using System.Drawing;

namespace Spectralis.Audio
{
    public class TrackInfo
    {
        public string FilePath { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string Genre { get; set; }
        public int Year { get; set; }
        public uint TrackNumber { get; set; }
        public TimeSpan Duration { get; set; }
        public int Bitrate { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public string Format { get; set; }
        public Image AlbumArt { get; set; }

        public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
            ? System.IO.Path.GetFileNameWithoutExtension(FilePath)
            : Title;

        public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "Unknown Artist" : Artist;

        public override string ToString() => $"{DisplayArtist} - {DisplayTitle}";
    }
}
