namespace Spectralis;

internal sealed class ScrobbleRecord
{
    public string Title     { get; set; } = "";
    public string Artist    { get; set; } = "";
    public string Album     { get; set; } = "";
    public long   Timestamp { get; set; }  // Unix epoch seconds
    public double Duration  { get; set; }
}
