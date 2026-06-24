using System.IO;
using System.Text;

namespace Spectralis;

internal static class M3uParser
{
    // Import M3U or M3U8 — returns list of absolute file paths.
    public static List<string> Import(string filePath)
    {
        var dir   = System.IO.Path.GetDirectoryName(filePath) ?? "";
        var paths = new List<string>();

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

            // Make absolute if relative
            var abs = System.IO.Path.IsPathRooted(line)
                ? line
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, line));

            if (!string.IsNullOrEmpty(abs))
                paths.Add(abs);
        }

        return paths;
    }

    // Import to PlaylistItems, preserving #EXTINF metadata where available.
    public static List<PlaylistItem> ImportItems(string filePath)
    {
        var dir   = System.IO.Path.GetDirectoryName(filePath) ?? "";
        var items = new List<PlaylistItem>();

        string? pendingArtist = null;
        string? pendingTitle  = null;
        double  pendingDur    = 0;

        foreach (var rawLine in File.ReadLines(filePath))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                ParseExtinf(line, out pendingDur, out pendingArtist, out pendingTitle);
                continue;
            }

            if (line.StartsWith('#')) continue;

            var abs = System.IO.Path.IsPathRooted(line)
                ? line
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, line));

            items.Add(new PlaylistItem
            {
                Path            = abs,
                Title           = pendingTitle,
                Artist          = pendingArtist,
                DurationSeconds = pendingDur,
            });

            pendingArtist = null;
            pendingTitle  = null;
            pendingDur    = 0;
        }

        return items;
    }

    // Export playlist items to M3U8 (UTF-8, extended format).
    public static void Export(string filePath, IEnumerable<PlaylistItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");

        foreach (var item in items)
        {
            var dur    = (int)Math.Round(item.DurationSeconds);
            var artist = item.Artist ?? "";
            var title  = item.Title  ?? System.IO.Path.GetFileNameWithoutExtension(item.Path);
            var label  = string.IsNullOrEmpty(artist) ? title : $"{artist} - {title}";
            sb.AppendLine($"#EXTINF:{dur},{label}");
            sb.AppendLine(item.Path);
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    // Export raw paths (no metadata).
    public static void ExportPaths(string filePath, IEnumerable<string> paths)
    {
        var sb = new StringBuilder("#EXTM3U\n");
        foreach (var p in paths)
            sb.AppendLine(p);
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static void ParseExtinf(string line, out double duration, out string? artist, out string? title)
    {
        // Format: #EXTINF:<duration>,<artist> - <title>
        duration = 0;
        artist   = null;
        title    = null;

        var rest = line["#EXTINF:".Length..].Trim();
        var commaIdx = rest.IndexOf(',');
        if (commaIdx < 0) return;

        double.TryParse(rest[..commaIdx], out duration);
        var label = rest[(commaIdx + 1)..].Trim();

        var dashIdx = label.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIdx >= 0)
        {
            artist = label[..dashIdx].Trim();
            title  = label[(dashIdx + 3)..].Trim();
        }
        else
        {
            title = label;
        }
    }
}
