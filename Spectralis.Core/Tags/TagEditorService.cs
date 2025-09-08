using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Spectralis.Core.Models;
using TagLib;

namespace Spectralis.Core.Tags
{
    public class TagEditorService : ITagEditor
    {
        private static readonly HashSet<string> _supported = new(StringComparer.OrdinalIgnoreCase)
            { ".mp3", ".flac", ".ogg", ".m4a", ".aac", ".opus", ".wav", ".wv", ".ape", ".mp4" };

        public Task<bool> CanEditAsync(string filePath)
            => Task.FromResult(_supported.Contains(Path.GetExtension(filePath)));

        public async Task<TrackInfo> ReadTagsAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                using var f = TagLib.File.Create(filePath);
                var t = f.Tag;
                return new TrackInfo
                {
                    FilePath = filePath,
                    Title = t.Title ?? Path.GetFileNameWithoutExtension(filePath),
                    Artist = t.FirstPerformer ?? "",
                    AlbumArtist = t.FirstAlbumArtist ?? "",
                    Album = t.Album ?? "",
                    Genre = t.FirstGenre ?? "",
                    Year = (int)t.Year,
                    TrackNumber = (int)t.Track,
                    DiscNumber = (int)t.Disc,
                    Duration = f.Properties.Duration,
                    CoverArtBytes = t.Pictures?.Length > 0 ? t.Pictures[0].Data.Data : null
                };
            });
        }

        public async Task WriteTagsAsync(string filePath, TrackInfo tags)
        {
            await Task.Run(() =>
            {
                string backup = filePath + ".bak";
                System.IO.File.Copy(filePath, backup, overwrite: true);
                try
                {
                    using var f = TagLib.File.Create(filePath);
                    f.Tag.Title = tags.Title;
                    f.Tag.Performers = string.IsNullOrEmpty(tags.Artist) ? Array.Empty<string>() : new[] { tags.Artist };
                    f.Tag.AlbumArtists = string.IsNullOrEmpty(tags.AlbumArtist) ? Array.Empty<string>() : new[] { tags.AlbumArtist };
                    f.Tag.Album = tags.Album;
                    f.Tag.Genres = string.IsNullOrEmpty(tags.Genre) ? Array.Empty<string>() : new[] { tags.Genre };
                    f.Tag.Year = tags.Year >= 0 ? (uint)tags.Year : 0;
                    f.Tag.Track = tags.TrackNumber >= 0 ? (uint)tags.TrackNumber : 0;
                    f.Tag.Disc = tags.DiscNumber >= 0 ? (uint)tags.DiscNumber : 0;
                    f.Save();
                    System.IO.File.Delete(backup);
                }
                catch
                {
                    if (System.IO.File.Exists(backup))
                        System.IO.File.Copy(backup, filePath, overwrite: true);
                    throw;
                }
            });
        }
    }
}
