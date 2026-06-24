namespace Spectralis;

internal sealed class Playlist
{
    public Guid     Id        { get; init; } = Guid.NewGuid();
    public string   Name      { get; set; }  = "New Playlist";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<PlaylistItem> Items { get; set; } = [];
}

internal sealed class PlaylistItem
{
    public string   Path            { get; set; } = "";
    public DateTime AddedAt         { get; set; } = DateTime.UtcNow;
    public string?  Title           { get; set; }
    public string?  Artist          { get; set; }
    public double   DurationSeconds { get; set; }
}
