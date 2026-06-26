namespace Spectralis;

internal sealed class SmartPlaylist
{
    public Guid      Id              { get; init; } = Guid.NewGuid();
    public string    Name            { get; set; }  = "New Smart Playlist";
    public DateTime  CreatedAt       { get; init; } = DateTime.UtcNow;
    public List<SmartRule>  Rules    { get; set; }  = [];
    public SmartMatchMode   Match    { get; set; }  = SmartMatchMode.All;
    public int       Limit           { get; set; }  = 0;   // 0 = unlimited
    public SmartSortField   SortBy   { get; set; }  = SmartSortField.DateAdded;
    public bool      SortDescending  { get; set; }  = true;
}

internal sealed class SmartRule
{
    public SmartRuleField    Field    { get; set; } = SmartRuleField.Title;
    public SmartRuleOp       Op       { get; set; } = SmartRuleOp.Contains;
    public string            Value    { get; set; } = "";
}

internal enum SmartMatchMode   { All, Any }
internal enum SmartSortField   { Title, Artist, Album, Year, PlayCount, LastPlayed, DateAdded, Duration }
internal enum SmartRuleField   { Title, Artist, Album, AlbumArtist, Genre, Year, PlayCount, Duration }
internal enum SmartRuleOp      { Contains, NotContains, Is, IsNot, GreaterThan, LessThan }
