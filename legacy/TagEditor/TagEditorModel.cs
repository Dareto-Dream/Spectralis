namespace Spectralis;

internal sealed class TagEditorModel
{
    public string FilePath { get; init; } = "";
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? AlbumArtist { get; set; }
    public string? Album { get; set; }
    public uint TrackNumber { get; set; }
    public uint DiscNumber { get; set; }
    public uint Year { get; set; }
    public string? Genre { get; set; }
    public string? Comment { get; set; }
    public string? Composer { get; set; }
    public uint BPM { get; set; }
    public byte[]? CoverArt { get; set; }
}
