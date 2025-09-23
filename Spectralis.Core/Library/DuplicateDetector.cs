using System;
using System.Collections.Generic;
using System.Linq;
using Spectralis.Core.Models;

namespace Spectralis.Core.Library
{
    public class DuplicateGroup
    {
        public List<TrackInfo> Tracks { get; set; } = new();
        public string MatchKey { get; set; } = string.Empty;
        public DuplicateMatchReason Reason { get; set; }
    }

    public enum DuplicateMatchReason { TitleArtist, FileSizeAndDuration, SameFilename }

    public class DuplicateDetector
    {
        public List<DuplicateGroup> FindDuplicates(IEnumerable<TrackInfo> tracks)
        {
            var result = new List<DuplicateGroup>();
            var list = tracks.ToList();

            var byTitleArtist = list
                .GroupBy(t => $"{Normalize(t.Title)}||{Normalize(t.Artist)}")
                .Where(g => g.Count() > 1);

            foreach (var g in byTitleArtist)
            {
                result.Add(new DuplicateGroup
                {
                    MatchKey = g.Key,
                    Reason = DuplicateMatchReason.TitleArtist,
                    Tracks = g.ToList()
                });
            }

            var bySize = list
                .Where(t => t.FileSizeBytes > 0)
                .GroupBy(t => $"{t.FileSizeBytes}||{(int)t.Duration.TotalSeconds}")
                .Where(g => g.Count() > 1);

            foreach (var g in bySize)
            {
                if (result.Any(r => r.Tracks.All(t => g.Any(gt => gt.FilePath == t.FilePath))))
                    continue;
                result.Add(new DuplicateGroup
                {
                    MatchKey = g.Key,
                    Reason = DuplicateMatchReason.FileSizeAndDuration,
                    Tracks = g.ToList()
                });
            }

            return result;
        }

        private static string Normalize(string s) =>
            s.Trim().ToLowerInvariant().Replace("  ", " ");
    }
}
