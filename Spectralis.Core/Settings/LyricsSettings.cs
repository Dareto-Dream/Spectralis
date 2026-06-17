namespace Spectralis.Core.Settings
{
    public class LyricsSettings
    {
        public bool Enabled { get; set; } = true;
        public bool ShowAnnotations { get; set; } = true;
        public bool ShowWordHighlight { get; set; } = true;
        public int FontSize { get; set; } = 22;
        public string HighlightColor { get; set; } = "#FF6EC7";
        public double SyncOffsetMs { get; set; } = 0;
    }
}
