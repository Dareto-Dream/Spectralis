namespace Spectralis.Core.Podcasts;

/// <summary>
/// Resolves chapters for a track. Precedence: a <c>.chapters.json</c> sidecar, then a
/// <c>.cue</c> sidecar, then embedded chapters (ID3v2 <c>CHAP</c> for MP3, Nero <c>chpl</c>
/// for MP4/M4A/M4B). The first non-empty result wins. Never throws.
/// </summary>
public static class ChapterLoader
{
    private const long MaxSidecarBytes = 4 * 1024 * 1024;

    public static ChapterList LoadForTrack(string audioPath, TimeSpan? trackDuration = null)
    {
        var (chapters, label) = LoadRaw(audioPath);
        if (chapters.Count == 0)
        {
            return ChapterList.Empty;
        }

        return new ChapterList(Normalize(chapters, trackDuration), label);
    }

    private static (IReadOnlyList<Chapter> Chapters, string Label) LoadRaw(string audioPath)
    {
        var json = ReadFirstExisting(audioPath, ".chapters.json");
        if (json is not null)
        {
            var parsed = PodloveChapterParser.Parse(json);
            if (parsed.Count > 0)
            {
                return (parsed, "chapters.json");
            }
        }

        var cue = ReadFirstExisting(audioPath, ".cue");
        if (cue is not null)
        {
            var parsed = CueSheetParser.Parse(cue);
            if (parsed.Count > 0)
            {
                return (parsed, "CUE");
            }
        }

        var extension = Path.GetExtension(audioPath).ToLowerInvariant();
        var embedded = extension switch
        {
            ".mp3" => Id3ChapterReader.Read(audioPath),
            ".m4a" or ".m4b" or ".mp4" or ".m4p" => Mp4ChapterReader.Read(audioPath),
            _ => Array.Empty<Chapter>(),
        };

        return embedded.Count > 0 ? (embedded, "Embedded") : (Array.Empty<Chapter>(), string.Empty);
    }

    private static IReadOnlyList<Chapter> Normalize(IReadOnlyList<Chapter> chapters, TimeSpan? trackDuration)
    {
        var ordered = chapters
            .Where(c => c.Start >= TimeSpan.Zero)
            .Where(c => trackDuration is null || c.Start < trackDuration.Value)
            .OrderBy(c => c.Start)
            .ToList();

        var result = new List<Chapter>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var end = ordered[i].End
                ?? (i + 1 < ordered.Count ? ordered[i + 1].Start : trackDuration);
            var title = string.IsNullOrWhiteSpace(ordered[i].Title) ? $"Chapter {i + 1}" : ordered[i].Title.Trim();
            result.Add(ordered[i] with { End = end, Title = title });
        }

        return result;
    }

    /// <summary>Tries <c>track.mp3.suffix</c> then <c>track.suffix</c>.</summary>
    private static string? ReadFirstExisting(string audioPath, string suffix)
    {
        return ReadSmallFile(audioPath + suffix)
            ?? ReadSmallFile(Path.ChangeExtension(audioPath, suffix));
    }

    private static string? ReadSmallFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= MaxSidecarBytes ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }
}
