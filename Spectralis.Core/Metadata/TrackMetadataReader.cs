using Spectralis.Core.Common;
using Spectralis.Core.Embedded;

namespace Spectralis.Core.Metadata;

/// <summary>
/// TagLibSharp wrapper. Never throws on malformed files — a track with broken
/// tags still plays and still appears in the library (file-name fallback).
/// </summary>
public static class TrackMetadataReader
{
    public static TrackInfo Read(string path)
    {
        var info = new TrackInfo { SourcePath = path };

        try
        {
            info = info with { FileSizeBytes = new FileInfo(path).Length };
        }
        catch
        {
            // missing/virtual file — leave size at 0
        }

        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            var props = file.Properties;
            var embedded = EmbeddedModuleReader.TryReadAll(
                file.GetTag(TagLib.TagTypes.Id3v2, false) as TagLib.Id3v2.Tag);

            var picture = tag.Pictures is { Length: > 0 } ? tag.Pictures[0] : null;

            return info with
            {
                Title = tag.Title?.Trim() ?? string.Empty,
                Artist = FirstNonEmpty(tag.JoinedPerformers, tag.JoinedAlbumArtists),
                Album = tag.Album?.Trim() ?? string.Empty,
                AlbumArtist = tag.JoinedAlbumArtists?.Trim() ?? string.Empty,
                Genre = tag.JoinedGenres?.Trim() ?? string.Empty,
                TrackNumber = tag.Track,
                DiscNumber = tag.Disc,
                Year = tag.Year,
                Bpm = tag.BeatsPerMinute > 0 ? tag.BeatsPerMinute : null,
                Duration = props?.Duration ?? TimeSpan.Zero,
                BitrateKbps = props?.AudioBitrate ?? 0,
                SampleRateHz = props?.AudioSampleRate ?? 0,
                Channels = props?.AudioChannels ?? 0,
                CoverArt = picture?.Data?.Data,
                CoverArtMimeType = picture?.MimeType,
                EmbeddedVisualizer = embedded.Visualizer,
                EmbeddedHtml = embedded.Html,
                EmbeddedMarkdown = embedded.Markdown,
                EmbeddedVideo = embedded.Video,
                EmbeddedTheme = embedded.Theme,
            };
        }
        catch
        {
            // Corrupt or unsupported tags must not break playback or library scans.
            return info;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}
