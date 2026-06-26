using System.Linq;
using TagLib;
using TagLib.Id3v2;

namespace Spectralis;

internal static class AudioMetadataReader
{
    public static AudioFileMetadata Read(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            var id3Tag = file.GetTag(TagTypes.Id3v2, false) as TagLib.Id3v2.Tag;
            var (embeddedVisualizer, embeddedHtml, embeddedMarkdown, embeddedVideo) = EmbeddedVisualizerMetadataReader.TryReadAll(id3Tag);
            var embeddedTheme = EmbeddedThemeMetadataReader.Read(id3Tag);

            return new AudioFileMetadata(
                Normalize(tag.Title),
                FirstNonEmpty(JoinDistinct(tag.Performers), JoinDistinct(tag.AlbumArtists), JoinDistinct(tag.Composers)),
                Normalize(tag.Album),
                ExtractAlbumArt(tag.Pictures),
                ExtractLyrics(tag, id3Tag, path, embeddedVisualizer),
                embeddedVisualizer,
                embeddedTheme,
                embeddedHtml,
                embeddedMarkdown,
                embeddedVideo);
        }
        catch
        {
            return AudioFileMetadata.Empty;
        }
    }

    private static string? JoinDistinct(string[] values)
    {
        var normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0 ? null : string.Join(", ", normalized);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static byte[]? ExtractAlbumArt(IPicture[] pictures)
    {
        var picture = pictures.FirstOrDefault(static candidate => candidate.Type == PictureType.FrontCover)
            ?? pictures.FirstOrDefault();

        return picture?.Data?.Data is { Length: > 0 } data
            ? data.ToArray()
            : null;
    }

    private static LyricsDocument? ExtractLyrics(
        TagLib.Tag tag,
        TagLib.Id3v2.Tag? id3Tag,
        string path,
        EmbeddedVisualizerContext? embeddedVisualizer)
    {
        var embeddedLyrics = ExtractEmbeddedLyrics(id3Tag, embeddedVisualizer);
        if (embeddedLyrics is not null)
        {
            return MergeExplanations(embeddedLyrics, path, id3Tag);
        }

        var tagLyrics = LrcParser.Parse(tag.Lyrics, "Embedded lyrics");
        if (tagLyrics is not null)
        {
            return MergeExplanations(tagLyrics, path, id3Tag);
        }

        var sidecarPath = Path.ChangeExtension(path, ".lrc");
        if (!System.IO.File.Exists(sidecarPath))
        {
            return null;
        }

        try
        {
            var lyrics = LrcParser.Parse(System.IO.File.ReadAllText(sidecarPath), "Sidecar LRC");
            if (lyrics is null)
            {
                return null;
            }

            return MergeExplanations(lyrics, path, id3Tag);
        }
        catch
        {
            return null;
        }
    }

    private static LyricsDocument? ExtractEmbeddedLyrics(
        TagLib.Id3v2.Tag? id3Tag,
        EmbeddedVisualizerContext? embeddedVisualizer)
    {
        var structuredLyrics = LrcParser.Parse(
            EmbeddedLyricsDataReader.TryExtractStructuredLyricsText(embeddedVisualizer),
            "Structured lyrics");
        if (structuredLyrics is not null)
        {
            return structuredLyrics;
        }

        if (id3Tag is null)
        {
            return null;
        }

        var embeddedLrc = id3Tag
            .GetFrames<UserTextInformationFrame>()
            .FirstOrDefault(static frame =>
                string.Equals(frame.Description, "LRC_SYNC", StringComparison.OrdinalIgnoreCase));

        var parsedEmbeddedLrc = LrcParser.Parse(JoinFrameText(embeddedLrc?.Text), "Embedded LRC");
        if (parsedEmbeddedLrc is not null)
        {
            return parsedEmbeddedLrc;
        }

        var unsynchronisedLyrics = id3Tag
            .GetFrames<UnsynchronisedLyricsFrame>()
            .Select(static frame => frame.Text)
            .FirstOrDefault(static text => !string.IsNullOrWhiteSpace(text));

        return LrcParser.Parse(unsynchronisedLyrics, "Embedded lyrics");
    }

    private static string? JoinFrameText(string[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return null;
        }

        var normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();

        return normalized.Length == 0 ? null : string.Join(Environment.NewLine, normalized);
    }

    private static LyricsDocument MergeExplanations(LyricsDocument lyrics, string audioPath, TagLib.Id3v2.Tag? id3Tag)
    {
        var explanations = ReadEmbeddedExplanations(id3Tag);
        if (explanations.Count == 0)
        {
            explanations = ReadSidecarExplanations(audioPath);
        }

        return explanations.Count == 0
            ? lyrics
            : ApplyExplanations(lyrics, explanations);
    }

    private static Dictionary<string, string> ReadEmbeddedExplanations(TagLib.Id3v2.Tag? id3Tag)
    {
        if (id3Tag is null)
        {
            return [];
        }

        try
        {
            var explanationFrame = id3Tag
                .GetFrames<UserTextInformationFrame>()
                .FirstOrDefault(static frame =>
                    string.Equals(frame.Description, "LYRIC_EXPLANATIONS", StringComparison.OrdinalIgnoreCase));

            if (explanationFrame?.Text == null || explanationFrame.Text.Length == 0)
            {
                return [];
            }

            var explanationJson = string.Join("", explanationFrame.Text);
            return LyricsExplanationParser.Parse(explanationJson);
        }
        catch
        {
            return [];
        }
    }

    private static Dictionary<string, string> ReadSidecarExplanations(string audioPath)
    {
        var explanationPath = Path.ChangeExtension(audioPath, ".lrc.json");
        if (!System.IO.File.Exists(explanationPath))
        {
            return [];
        }

        try
        {
            var explanationJson = System.IO.File.ReadAllText(explanationPath);
            return LyricsExplanationParser.Parse(explanationJson);
        }
        catch
        {
            return [];
        }
    }

    private static LyricsDocument ApplyExplanations(
        LyricsDocument lyrics,
        IReadOnlyDictionary<string, string> explanations)
    {
        var linesWithExplanations = lyrics.Lines
            .Select(line =>
            {
                var explanation = LyricsExplanationParser.GetExplanationForTimestamp(explanations, line.StartTime);
                return new LyricsLine(line.StartTime, line.Text, line.Segments, explanation);
            })
            .ToList();

        return new LyricsDocument(linesWithExplanations, lyrics.Metadata, lyrics.OffsetMilliseconds, lyrics.SourceLabel, lyrics.IsDescription);
    }
}

internal sealed record AudioFileMetadata(
    string? Title,
    string? Artist,
    string? Album,
    byte[]? AlbumArtBytes,
    LyricsDocument? Lyrics,
    EmbeddedVisualizerContext? EmbeddedVisualizer,
    EmbeddedThemeContext? EmbeddedTheme,
    EmbeddedHtmlContext? EmbeddedHtml,
    EmbeddedMarkdownContext? EmbeddedMarkdown,
    EmbeddedVideoContext? EmbeddedVideo)
{
    public static AudioFileMetadata Empty { get; } = new(null, null, null, null, null, null, null, null, null, null);
}
