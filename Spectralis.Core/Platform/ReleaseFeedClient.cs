using System.Text.RegularExpressions;

namespace Spectralis.Core.Platform;

public sealed record ReleaseFeedResult(bool IsUpdateAvailable, string? LatestVersion, string? DownloadUrl, string? ErrorMessage);

public static class ReleaseFeedClient
{
    private const string FeedUrl = "https://cdn.deltavdevs.com/spectralis/RELEASES";
    private const string DownloadPageUrl = "https://spectralis.deltavdevs.com";

    public static async Task<ReleaseFeedResult> CheckAsync(string? currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            var text = await http.GetStringAsync(FeedUrl, cancellationToken);
            var latestVersion = ParseLatestVersion(text);
            if (latestVersion is null)
                return new ReleaseFeedResult(false, null, null, "Could not parse release feed.");

            var isNewer = IsNewerVersion(latestVersion, currentVersion);
            return new ReleaseFeedResult(isNewer, latestVersion, DownloadPageUrl, null);
        }
        catch (OperationCanceledException)
        {
            return new ReleaseFeedResult(false, null, null, "Update check was cancelled.");
        }
        catch (Exception ex)
        {
            return new ReleaseFeedResult(false, null, null, ex.Message);
        }
    }

    private static string? ParseLatestVersion(string releasesText)
    {
        // Squirrel RELEASES format: SHA1Hash PackageName-Version-full.nupkg FileSize
        var best = default(Version);
        var bestStr = default(string);
        foreach (var line in releasesText.Split('\n'))
        {
            var match = Regex.Match(line.Trim(), @"Spectralis-(\d+\.\d+(?:\.\d+(?:\.\d+)?)?)-full\.nupkg", RegexOptions.IgnoreCase);
            if (!match.Success) continue;
            if (Version.TryParse(match.Groups[1].Value, out var v) && (best is null || v > best))
            {
                best = v;
                bestStr = match.Groups[1].Value;
            }
        }
        return bestStr;
    }

    private static bool IsNewerVersion(string latestVersion, string? currentVersion)
    {
        if (!Version.TryParse(latestVersion, out var latest)) return false;
        if (string.IsNullOrWhiteSpace(currentVersion) || !Version.TryParse(currentVersion, out var current)) return true;
        return latest > current;
    }
}
