using Spectralis.Core.Common;

namespace Spectralis.Core.Podcasts;

/// <summary>
/// Heuristic "is this a podcast / audiobook?" check used during library scans. Any signal is
/// enough; the user's manual override (stored separately) always wins over this.
/// </summary>
public static class PodcastDetector
{
    private static readonly string[] GenreMarkers = { "podcast", "audiobook", "audio book", "spoken" };

    public static bool DetectAuto(TrackInfo track, string path, IReadOnlyCollection<string>? podcastFolders)
    {
        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".m4b", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(track.Genre) &&
            GenreMarkers.Any(marker => track.Genre.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return podcastFolders is not null && IsUnderAnyFolder(path, podcastFolders);
    }

    public static bool IsUnderAnyFolder(string path, IReadOnlyCollection<string> folders)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            string root;
            try
            {
                root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                continue;
            }

            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
