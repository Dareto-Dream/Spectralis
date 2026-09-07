using TagLib.Id3v2;

namespace Spectralis.Core.Podcasts;

/// <summary>
/// Reads embedded ID3v2 chapters (<c>CHAP</c> frames, ordered by a top-level <c>CTOC</c>
/// when present). TagLibSharp parses the frames natively; titles come from the <c>TIT2</c>
/// sub-frame and links from <c>WXXX</c>.
/// </summary>
public static class Id3ChapterReader
{
    public static IReadOnlyList<Chapter> Read(string audioPath)
    {
        try
        {
            using var file = TagLib.File.Create(audioPath);
            if (file.GetTag(TagLib.TagTypes.Id3v2, false) is not Tag tag)
            {
                return Array.Empty<Chapter>();
            }

            var chapterFrames = tag.GetFrames<ChapterFrame>().ToList();
            if (chapterFrames.Count == 0)
            {
                return Array.Empty<Chapter>();
            }

            var ordered = OrderByTableOfContents(tag, chapterFrames);

            var parsed = ordered
                .Select(frame => (
                    Start: TimeSpan.FromMilliseconds(frame.StartMilliseconds),
                    End: frame.EndMilliseconds > frame.StartMilliseconds
                        ? (TimeSpan?)TimeSpan.FromMilliseconds(frame.EndMilliseconds)
                        : null,
                    Title: SubFrameText(frame, "TIT2"),
                    Url: SubFrameUrl(frame)))
                .OrderBy(c => c.Start)
                .ToList();

            var result = new List<Chapter>(parsed.Count);
            for (var i = 0; i < parsed.Count; i++)
            {
                var end = parsed[i].End ?? (i + 1 < parsed.Count ? parsed[i + 1].Start : null);
                var title = string.IsNullOrWhiteSpace(parsed[i].Title) ? $"Chapter {i + 1}" : parsed[i].Title!;
                result.Add(new Chapter(parsed[i].Start, end, title, parsed[i].Url));
            }

            return result;
        }
        catch
        {
            return Array.Empty<Chapter>();
        }
    }

    private static IEnumerable<ChapterFrame> OrderByTableOfContents(Tag tag, List<ChapterFrame> chapterFrames)
    {
        var toc = tag.GetFrames<TableOfContentsFrame>().FirstOrDefault(t => t.IsTopLevel)
            ?? tag.GetFrames<TableOfContentsFrame>().FirstOrDefault();
        if (toc is null || toc.ChapterIds.Count == 0)
        {
            return chapterFrames;
        }

        var byId = chapterFrames
            .GroupBy(f => f.Id ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var ordered = toc.ChapterIds
            .Where(id => id is not null && byId.ContainsKey(id))
            .Select(id => byId[id])
            .ToList();

        // Any CHAP frames not referenced by the TOC still get included, appended.
        ordered.AddRange(chapterFrames.Where(f => !ordered.Contains(f)));
        return ordered.Count > 0 ? ordered : chapterFrames;
    }

    private static string? SubFrameText(ChapterFrame frame, string frameId)
    {
        var text = frame.SubFrames
            .OfType<TextInformationFrame>()
            .FirstOrDefault(f => f.FrameId.ToString() == frameId)?
            .Text;
        return text is { Length: > 0 } ? text[0]?.Trim() : null;
    }

    private static string? SubFrameUrl(ChapterFrame frame)
    {
        var url = frame.SubFrames.OfType<UserUrlLinkFrame>().FirstOrDefault()?.Text;
        if (url is { Length: > 0 } && !string.IsNullOrWhiteSpace(url[0]))
        {
            return url[0].Trim();
        }

        var plain = frame.SubFrames.OfType<UrlLinkFrame>().FirstOrDefault()?.Text;
        return plain is { Length: > 0 } && !string.IsNullOrWhiteSpace(plain[0]) ? plain[0].Trim() : null;
    }
}
