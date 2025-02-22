using System;
using System.Drawing;
using System.IO;
using TagLib;

namespace Spectralis.Audio
{
    public static class MetadataExtractor
    {
        public static TrackInfo Extract(string filePath)
        {
            var info = new TrackInfo
            {
                FilePath = filePath,
                Format = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant()
            };

            try
            {
                var file = TagLib.File.Create(filePath);

                info.Title = file.Tag.Title;
                info.Artist = file.Tag.FirstPerformer ?? file.Tag.FirstAlbumArtist;
                info.Album = file.Tag.Album;
                info.Genre = file.Tag.FirstGenre;
                info.Year = (int)file.Tag.Year;
                info.TrackNumber = file.Tag.Track;
                info.Duration = file.Properties.Duration;
                info.Bitrate = file.Properties.AudioBitrate;
                info.SampleRate = file.Properties.AudioSampleRate;
                info.Channels = file.Properties.AudioChannels;

                var coverArt = file.Tag.Pictures[0];
                using var ms = new MemoryStream(coverArt.Data.Data);
                info.AlbumArt = Image.FromStream(ms);
            }
            catch (Exception)
            {
            }

            return info;
        }

        public static TrackInfo ExtractBasic(string filePath)
        {
            return new TrackInfo
            {
                FilePath = filePath,
                Title = Path.GetFileNameWithoutExtension(filePath),
                Format = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant()
            };
        }
    }
}
