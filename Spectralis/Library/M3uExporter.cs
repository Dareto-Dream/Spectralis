using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Spectralis.Library
{
    public static class M3uExporter
    {
        public static void Export(string outputPath, IEnumerable<LibraryTrack> tracks)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#EXTM3U");

            foreach (var track in tracks)
            {
                int durationSec = (int)(track.DurationMs / 1000);
                string artist = track.Artist ?? "Unknown";
                string title = track.DisplayTitle;
                sb.AppendLine($"#EXTINF:{durationSec},{artist} - {title}");
                sb.AppendLine(track.Path);
            }

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        }

        public static IList<string> Import(string m3uPath)
        {
            var paths = new List<string>();
            if (!File.Exists(m3uPath)) return paths;

            string dir = Path.GetDirectoryName(m3uPath);
            foreach (var line in File.ReadAllLines(m3uPath))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed)) continue;

                string path = Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(dir, trimmed);
                if (File.Exists(path)) paths.Add(path);
            }

            return paths;
        }
    }
}
