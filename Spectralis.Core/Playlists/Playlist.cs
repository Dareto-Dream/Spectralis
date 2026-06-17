using System;
using System.Collections.Generic;

namespace Spectralis.Core.Playlists
{
    public enum SmartRuleField { Title, Artist, Album, Genre, Year, Duration, PlayCount }
    public enum SmartRuleOperator { Contains, NotContains, Equals, StartsWith, GreaterThan, LessThan }

    public class SmartPlaylistRule
    {
        public SmartRuleField Field { get; set; }
        public SmartRuleOperator Operator { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    public class Playlist
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public bool IsSmart { get; set; }
        public List<SmartPlaylistRule> Rules { get; set; } = new();
        public bool MatchAll { get; set; } = true;
        public int? Limit { get; set; }
        public string SortBy { get; set; } = "artist";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class PlaylistTrack
    {
        public Guid PlaylistId { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public int Position { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
