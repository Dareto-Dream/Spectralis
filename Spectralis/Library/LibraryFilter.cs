using System;
using System.Collections.Generic;
using System.Linq;

namespace Spectralis.Library
{
    public enum SortField { Title, Artist, Album, Year, Duration, DateAdded, PlayCount }
    public enum SortDirection { Ascending, Descending }

    public class LibraryFilter
    {
        public string SearchText { get; set; }
        public string ArtistFilter { get; set; }
        public string AlbumFilter { get; set; }
        public string GenreFilter { get; set; }
        public SortField Sort { get; set; } = SortField.Artist;
        public SortDirection Direction { get; set; } = SortDirection.Ascending;

        public IList<LibraryTrack> Apply(IList<LibraryTrack> tracks)
        {
            IEnumerable<LibraryTrack> result = tracks;

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var q = SearchText.ToLowerInvariant();
                result = result.Where(t =>
                    (t.Title ?? "").ToLowerInvariant().Contains(q) ||
                    (t.Artist ?? "").ToLowerInvariant().Contains(q) ||
                    (t.Album ?? "").ToLowerInvariant().Contains(q));
            }

            if (!string.IsNullOrWhiteSpace(ArtistFilter))
                result = result.Where(t => string.Equals(t.Artist, ArtistFilter, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(AlbumFilter))
                result = result.Where(t => string.Equals(t.Album, AlbumFilter, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(GenreFilter))
                result = result.Where(t => string.Equals(t.Genre, GenreFilter, StringComparison.OrdinalIgnoreCase));

            result = Sort switch
            {
                SortField.Title => Direction == SortDirection.Ascending ? result.OrderBy(t => t.DisplayTitle) : result.OrderByDescending(t => t.DisplayTitle),
                SortField.Artist => Direction == SortDirection.Ascending ? result.OrderBy(t => t.DisplayArtist).ThenBy(t => t.Album).ThenBy(t => t.TrackNumber) : result.OrderByDescending(t => t.DisplayArtist),
                SortField.Album => Direction == SortDirection.Ascending ? result.OrderBy(t => t.Album).ThenBy(t => t.TrackNumber) : result.OrderByDescending(t => t.Album),
                SortField.Year => Direction == SortDirection.Ascending ? result.OrderBy(t => t.Year) : result.OrderByDescending(t => t.Year),
                SortField.Duration => Direction == SortDirection.Ascending ? result.OrderBy(t => t.DurationMs) : result.OrderByDescending(t => t.DurationMs),
                SortField.DateAdded => Direction == SortDirection.Ascending ? result.OrderBy(t => t.DateAdded) : result.OrderByDescending(t => t.DateAdded),
                SortField.PlayCount => result.OrderByDescending(t => t.PlayCount),
                _ => result
            };

            return result.ToList();
        }
    }
}
