using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Core.Models;
using TagLib;

namespace Spectralis.Core.Audio
{
    public class MetadataExtractor : ITrackLoader
    {
        private static readonly string[] _extensions =
            { ".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".opus", ".wma", ".ape", ".mpc" };

        public IReadOnlyList<string> SupportedExtensions => _extensions;

        public bool CanLoad(string filePath) =>
            Array.IndexOf(_extensions, Path.GetExtension(filePath).ToLower()) >= 0;

        public Task<TrackInfo> LoadMetadataAsync(string filePath, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var info = new TrackInfo { FilePath = filePath };

                try
                {
                    using var file = TagLib.File.Create(filePath);
                    var tag = file.Tag;

                    info.Title = tag.Title ?? Path.GetFileNameWithoutExtension(filePath);
                    info.Artist = tag.FirstPerformer ?? string.Empty;
                    info.AlbumArtist = tag.FirstAlbumArtist ?? string.Empty;
                    info.Album = tag.Album ?? string.Empty;
                    info.Genre = tag.FirstGenre ?? string.Empty;
                    info.Year = (int)tag.Year;
                    info.TrackNumber = (int)tag.Track;
                    info.DiscNumber = (int)tag.Disc;
                    info.Duration = file.Properties.Duration;
                    info.Bitrate = file.Properties.AudioBitrate;
                    info.SampleRate = file.Properties.AudioSampleRate;
                    info.FileSizeBytes = new FileInfo(filePath).Length;

                    if (tag.Pictures != null && tag.Pictures.Length > 0)
                        info.CoverArtBytes = tag.Pictures[0].Data.Data;
                }
                catch { }

                return info;
            }, ct);
        }
    }
}
