using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Spectralis.Core.Models;

namespace Spectralis.Core.Library
{
    public class LibraryImportExport
    {
        public async Task ExportM3UAsync(string outputPath, IEnumerable<TrackInfo> tracks)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#EXTM3U");
            foreach (var t in tracks)
            {
                sb.AppendLine($"#EXTINF:{(int)t.Duration.TotalSeconds},{t.Artist} - {t.Title}");
                sb.AppendLine(t.FilePath);
            }
            await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
        }

        public async Task<List<string>> ImportM3UAsync(string inputPath)
        {
            var paths = new List<string>();
            string? dir = Path.GetDirectoryName(inputPath);

            foreach (string line in await File.ReadAllLinesAsync(inputPath, Encoding.UTF8))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

                if (Path.IsPathRooted(trimmed))
                    paths.Add(trimmed);
                else if (dir != null)
                    paths.Add(Path.GetFullPath(Path.Combine(dir, trimmed)));
            }

            return paths;
        }

        public async Task ExportPLSAsync(string outputPath, IEnumerable<TrackInfo> tracks)
        {
            var list = new List<TrackInfo>(tracks);
            var sb = new StringBuilder();
            sb.AppendLine("[playlist]");
            for (int i = 0; i < list.Count; i++)
            {
                sb.AppendLine($"File{i + 1}={list[i].FilePath}");
                sb.AppendLine($"Title{i + 1}={list[i].Artist} - {list[i].Title}");
                sb.AppendLine($"Length{i + 1}={(int)list[i].Duration.TotalSeconds}");
            }
            sb.AppendLine($"NumberOfEntries={list.Count}");
            sb.AppendLine("Version=2");
            await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8);
        }
    }
}
