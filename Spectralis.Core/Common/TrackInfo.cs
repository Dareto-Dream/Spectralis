namespace Spectralis.Core.Common;

using Spectralis.Core.Embedded;

/// <summary>
/// Immutable description of a playable track, independent of where it came from
/// (local file, capsule member, remote stream).
/// </summary>
public sealed record TrackInfo
{
    public required string SourcePath { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string Album { get; init; } = string.Empty;
    public string AlbumArtist { get; init; } = string.Empty;
    public string Genre { get; init; } = string.Empty;
    public uint TrackNumber { get; init; }
    public uint DiscNumber { get; init; }
    public uint Year { get; init; }
    public TimeSpan Duration { get; init; }
    public int BitrateKbps { get; init; }
    public int SampleRateHz { get; init; }
    public int Channels { get; init; }
    public long FileSizeBytes { get; init; }
    public string FormatName { get; init; } = string.Empty;
    public double? Bpm { get; init; }
    public string? MusicalKey { get; init; }
    public byte[]? CoverArt { get; init; }
    public string? CoverArtMimeType { get; init; }
    public EmbeddedVisualizerContext? EmbeddedVisualizer { get; init; }
    public EmbeddedHtmlContext? EmbeddedHtml { get; init; }
    public EmbeddedMarkdownContext? EmbeddedMarkdown { get; init; }
    public EmbeddedVideoContext? EmbeddedVideo { get; init; }
    public EmbeddedThemeInfo? EmbeddedTheme { get; init; }

    public bool HasEmbeddedVisualizer => EmbeddedVisualizer is not null;
    public bool HasEmbeddedContent =>
        EmbeddedHtml is not null ||
        EmbeddedMarkdown is not null ||
        EmbeddedVideo is not null;

    /// <summary>Display title - falls back to the file name when no tag is present.</summary>
    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(Title) ? System.IO.Path.GetFileNameWithoutExtension(SourcePath) : Title;
}
