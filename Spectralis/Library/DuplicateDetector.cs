using System.Collections.Generic;
using System.Linq;

namespace Spectralis.Library
{
    public class DuplicateGroup
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public List<LibraryTrack> Tracks { get; set; } = new List<LibraryTrack>();
    }

    public static class DuplicateDetector
    {
        public static IList<DuplicateGroup> Find(IList<LibraryTrack> allTracks)
        {
            return allTracks
                .Where(t => !string.IsNullOrWhiteSpace(t.Title))
                .GroupBy(t => $"{t.Artist?.ToLowerInvariant()}|{t.Title?.ToLowerInvariant()}")
                .Where(g => g.Count() > 1)
                .Select(g => new DuplicateGroup
                {
                    Title = g.First().DisplayTitle,
                    Artist = g.First().DisplayArtist,
                    Tracks = g.ToList()
                })
                .ToList();
        }
    }
}
