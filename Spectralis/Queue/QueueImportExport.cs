using System;
using System.Collections.Generic;
using System.IO;
using Spectralis.Library;

namespace Spectralis.Queue
{
    public static class QueueImportExport
    {
        public static void ExportM3u(PlayQueue queue, string path)
        {
            using (var sw = new StreamWriter(path, false, System.Text.Encoding.UTF8))
            {
                sw.WriteLine("#EXTM3U");
                foreach (var item in queue.Items)
                {
                    if (item.Track == null) continue;
                    int secs = (int)item.Track.Duration.TotalSeconds;
                    string display = $"{item.Track.Artist} - {item.Track.Title}";
                    sw.WriteLine($"#EXTINF:{secs},{display}");
                    sw.WriteLine(item.Track.FilePath);
                }
            }
        }

        public static List<TrackInfo> ImportM3u(string path)
        {
            var result = new List<TrackInfo>();
            if (!File.Exists(path)) return result;

            string[] lines = File.ReadAllLines(path, System.Text.Encoding.UTF8);
            TrackInfo pending = null;

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line == "#EXTM3U") continue;

                if (line.StartsWith("#EXTINF:"))
                {
                    pending = new TrackInfo();
                    int comma = line.IndexOf(',');
                    if (comma >= 0)
                    {
                        string meta = line.Substring(comma + 1).Trim();
                        int dash = meta.IndexOf(" - ");
                        if (dash >= 0)
                        {
                            pending.Artist = meta.Substring(0, dash).Trim();
                            pending.Title = meta.Substring(dash + 3).Trim();
                        }
                        else
                        {
                            pending.Title = meta;
                        }
                        string durStr = line.Substring(8, comma - 8);
                        if (int.TryParse(durStr, out int dur))
                            pending.Duration = TimeSpan.FromSeconds(dur);
                    }
                }
                else if (!line.StartsWith("#"))
                {
                    if (pending == null) pending = new TrackInfo();
                    pending.FilePath = line;
                    result.Add(pending);
                    pending = null;
                }
            }

            return result;
        }
    }
}
