using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Spectralis.Library
{
    public class LibraryExportService
    {
        private readonly LibraryRepository _repo;

        public LibraryExportService(LibraryRepository repo)
        {
            _repo = repo;
        }

        public void ExportAllToM3u(string outputPath)
        {
            var tracks = _repo.GetAll();
            ExportToM3u(tracks, outputPath);
        }

        public void ExportToM3u(IEnumerable<LibraryTrack> tracks, string outputPath)
        {
            using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
            writer.WriteLine("#EXTM3U");
            foreach (var t in tracks)
            {
                var durationSec = (int)(t.DurationMs / 1000);
                var display = string.IsNullOrWhiteSpace(t.Title)
                    ? Path.GetFileNameWithoutExtension(t.Path)
                    : $"{t.DisplayArtist} - {t.DisplayTitle}";
                writer.WriteLine($"#EXTINF:{durationSec},{display}");
                writer.WriteLine(t.Path);
            }
        }

        public void ExportFilteredToM3u(string query, string outputPath)
        {
            var tracks = string.IsNullOrWhiteSpace(query)
                ? _repo.GetAll()
                : _repo.Search(query + "*");
            ExportToM3u(tracks, outputPath);
        }
    }
}
