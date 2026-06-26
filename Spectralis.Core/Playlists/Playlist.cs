namespace Spectralis.Core.Playlists;

public sealed class Playlist
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "New Playlist";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<PlaylistItem> Items { get; set; } = [];
}

public sealed class PlaylistItem
{
    public string Path { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public double DurationSeconds { get; set; }
}

public sealed class SmartPlaylist
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "New Smart Playlist";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<SmartRule> Rules { get; set; } = [];
    public SmartMatchMode Match { get; set; } = SmartMatchMode.All;
    public int Limit { get; set; }  // 0 = unlimited
    public SmartSortField SortBy { get; set; } = SmartSortField.DateAdded;
    public bool SortDescending { get; set; } = true;
}

public sealed class SmartRule
{
    public SmartRuleField Field { get; set; } = SmartRuleField.Title;
    public SmartRuleOp Op { get; set; } = SmartRuleOp.Contains;
    public string Value { get; set; } = "";
}

public enum SmartMatchMode { All, Any }
public enum SmartSortField { Title, Artist, Album, Year, PlayCount, LastPlayed, DateAdded, Duration }
public enum SmartRuleField { Title, Artist, Album, AlbumArtist, Genre, Year, PlayCount, Duration }
public enum SmartRuleOp { Contains, NotContains, Is, IsNot, GreaterThan, LessThan }
