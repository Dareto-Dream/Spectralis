namespace Spectralis;

public sealed record AudioTrackInfo(
    string FilePath,
    string DisplayName,
    string? Artist,
    string? Album,
    byte[]? AlbumArtBytes,
    LyricsDocument? Lyrics,
    EmbeddedVisualizerContext? EmbeddedVisualizer,
    EmbeddedThemeContext? EmbeddedTheme,
    EmbeddedHtmlContext? EmbeddedHtml,
    EmbeddedMarkdownContext? EmbeddedMarkdown,
    EmbeddedVideoContext? EmbeddedVideo,
    string FormatName,
    int Channels,
    int SourceSampleRate,
    int BitsPerSample,
    TimeSpan Duration,
    bool SuppressAppLyrics = false)
{
    public bool IsMidi =>
        string.Equals(FormatName, "MIDI", StringComparison.OrdinalIgnoreCase) ||
        FilePath.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) ||
        FilePath.EndsWith(".midi", StringComparison.OrdinalIgnoreCase) ||
        FilePath.EndsWith(".kar", StringComparison.OrdinalIgnoreCase);
}
