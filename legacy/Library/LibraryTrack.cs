namespace Spectralis;

internal sealed record LibraryTrack(
    string Path,
    string Title,
    string Artist,
    string Album,
    string AlbumArtist,
    string Genre,
    int Year,
    double DurationSeconds,
    int PlayCount,
    DateTime DateAdded,
    DateTime? LastPlayed,
    float? Bpm = null,
    string? Key = null)
{
    public string DisplayDuration
    {
        get
        {
            if (DurationSeconds <= 0) return "";
            var ts = TimeSpan.FromSeconds(DurationSeconds);
            return ts.TotalHours >= 1
                ? ts.ToString(@"h\:mm\:ss")
                : ts.ToString(@"m\:ss");
        }
    }
}
