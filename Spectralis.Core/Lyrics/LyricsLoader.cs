using TagLib.Id3v2;
using Spectralis.Core.Embedded;

namespace Spectralis.Core.Lyrics;

/// <summary>
/// Resolves lyrics for a track: embedded lyrics first (structured data block
/// from an embedded visualizer module, then the flat LRC_SYNC tag frame, then
/// standard USLT), falling back to a .lrc sidecar only if nothing is embedded.
/// Annotations come from an embedded LYRIC_EXPLANATIONS tag frame or a
/// .lrc.json sidecar, keyed by mm:ss.cc timestamps, and are attached to
/// matching lines.
/// </summary>
public static class LyricsLoader
{
    private const long MaxSidecarBytes = 4 * 1024 * 1024;
    private const string LrcSyncFrameDescription = "LRC_SYNC";
    private const string LyricExplanationsFrameDescription = "LYRIC_EXPLANATIONS";

    public static LyricsDocument? LoadForTrack(string audioPath)
    {
        var document = LoadEmbedded(audioPath) ?? LoadSidecar(audioPath);
        if (document is null)
        {
            return null;
        }

        var annotations = LoadAnnotations(audioPath);
        return annotations.Count == 0 ? document : ApplyAnnotations(document, annotations);
    }

    public static LyricsDocument? LoadSidecar(string audioPath)
    {
        var lrcPath = Path.ChangeExtension(audioPath, ".lrc");
        var text = ReadSmallFile(lrcPath);
        return text is null ? null : LrcParser.Parse(text, "LRC sidecar");
    }

    public static LyricsDocument? LoadEmbedded(string audioPath)
    {
        try
        {
            using var file = TagLib.File.Create(audioPath);
            var id3Tag = file.GetTag(TagLib.TagTypes.Id3v2, false) as Tag;

            var structuredText = TryExtractStructuredLyricsText(id3Tag);
            var structured = LrcParser.Parse(structuredText, "Structured lyrics");
            if (structured is not null)
            {
                return structured;
            }

            var lrcSyncText = TryGetUserTextFrame(id3Tag, LrcSyncFrameDescription);
            var lrcSync = LrcParser.Parse(lrcSyncText, "Embedded LRC");
            if (lrcSync is not null)
            {
                return lrcSync;
            }

            var lyrics = file.Tag.Lyrics;
            return string.IsNullOrWhiteSpace(lyrics) ? null : LrcParser.Parse(lyrics, "Embedded lyrics");
        }
        catch
        {
            return null;
        }
    }

    public static Dictionary<string, string> LoadAnnotations(string audioPath)
    {
        var embedded = TryLoadEmbeddedAnnotations(audioPath);
        if (embedded.Count > 0)
        {
            return embedded;
        }

        // Sidecar naming convention: "track.lrc.json" next to "track.mp3".
        var annotationPath = Path.ChangeExtension(audioPath, ".lrc.json");
        var text = ReadSmallFile(annotationPath);
        return text is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : LyricsExplanationParser.Parse(text);
    }

    private static Dictionary<string, string> TryLoadEmbeddedAnnotations(string audioPath)
    {
        try
        {
            using var file = TagLib.File.Create(audioPath);
            var id3Tag = file.GetTag(TagLib.TagTypes.Id3v2, false) as Tag;
            var json = TryGetUserTextFrame(id3Tag, LyricExplanationsFrameDescription);
            return json is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : LyricsExplanationParser.Parse(json);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Looks for lyrics inside an embedded visualizer module's data blocks
    /// (data refs/ids containing "lyric" or "lrc"), matching the legacy
    /// WinForms app's EmbeddedLyricsDataReader so delta-mp3 exports that bind
    /// lyrics through the module's dataRefs (rather than a flat LRC_SYNC
    /// frame) still resolve.
    /// </summary>
    private static string? TryExtractStructuredLyricsText(Tag? id3Tag)
    {
        if (id3Tag is null)
        {
            return null;
        }

        var visualizer = EmbeddedModuleReader.TryReadAll(id3Tag).Visualizer;
        if (visualizer is null)
        {
            return null;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataBlock in GetLyricsCandidates(visualizer, seenIds))
        {
            var lyricsText = TryExtractLyricsText(dataBlock);
            if (!string.IsNullOrWhiteSpace(lyricsText))
            {
                return lyricsText;
            }
        }

        return null;
    }

    private static IEnumerable<EmbeddedDataBlock> GetLyricsCandidates(
        EmbeddedVisualizerContext context,
        ISet<string> seenIds)
    {
        foreach (var dataRef in context.Module.DataRefs)
        {
            if (!LooksLikeLyricsReference(dataRef.Key) && !LooksLikeLyricsReference(dataRef.Value))
            {
                continue;
            }

            var dataBlock = context.GetDataByReference(dataRef.Value);
            if (dataBlock is not null && seenIds.Add(dataBlock.Id))
            {
                yield return dataBlock;
            }
        }

        foreach (var dataBlock in context.DataBlocks.Values)
        {
            if (LooksLikeLyricsReference(dataBlock.Id) && seenIds.Add(dataBlock.Id))
            {
                yield return dataBlock;
            }
        }
    }

    private static string? TryExtractLyricsText(EmbeddedDataBlock dataBlock)
    {
        if (LooksLikeLrcText(dataBlock.RawText))
        {
            return dataBlock.RawText.Trim();
        }

        var inlineText = dataBlock.TryGetString("lrc", "lyrics", "text", "content");
        return LooksLikeLrcText(inlineText) ? inlineText : null;
    }

    private static bool LooksLikeLyricsReference(string value) =>
        value.Contains("lyric", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("lrc", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeLrcText(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains('[', StringComparison.Ordinal) &&
        value.Contains(':', StringComparison.Ordinal);

    private static string? TryGetUserTextFrame(Tag? id3Tag, string description)
    {
        if (id3Tag is null)
        {
            return null;
        }

        var frame = id3Tag
            .GetFrames<UserTextInformationFrame>()
            .FirstOrDefault(candidate => string.Equals(candidate.Description, description, StringComparison.OrdinalIgnoreCase));

        return JoinFrameText(frame?.Text);
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

    private static LyricsDocument ApplyAnnotations(LyricsDocument document, IReadOnlyDictionary<string, string> annotations)
    {
        var annotatedLines = document.Lines.Select(line =>
        {
            var explanation = LyricsExplanationParser.GetExplanationForTimestamp(annotations, line.StartTime);
            return explanation is null
                ? line
                : new LyricsLine(line.StartTime, line.Text, line.Segments, explanation);
        });

        return new LyricsDocument(annotatedLines, document.Metadata, document.OffsetMilliseconds, document.SourceLabel);
    }

    private static string? ReadSmallFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxSidecarBytes)
            {
                return null;
            }

            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }
}
