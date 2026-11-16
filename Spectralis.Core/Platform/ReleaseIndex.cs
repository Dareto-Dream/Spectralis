using System.Text.Json;

namespace Spectralis.Core.Platform;

/// <summary>
/// One published version from the release index (<c>/spectralis/v1/releases</c>): where its update
/// feed and its installer live. The index is written by the release pipeline; every stable release
/// stays in it, which is what makes installing an older version possible, and the newest few betas.
/// </summary>
public sealed record ReleaseInfo(
    string Version,
    bool IsBeta,
    string Date,
    string FeedBase,
    string NotesUrl,
    string InstallerUrl)
{
    public string Label => IsBeta ? $"{Version} (beta)" : Version;
}

public static class ReleaseIndex
{
    public const string Path = "/spectralis/v1/releases";
    public const string Url = SpectralisEndpoints.ApiBase + Path;

    // The app installs whatever a feed base serves, so a feed base is only followed when it is one
    // of ours. A tampered index entry pointing anywhere else is dropped rather than trusted.
    private static readonly string[] TrustedFeedPrefixes =
    [
        "https://github.com/Dareto-Dream/Spectralis/releases/download/",
        "https://spectralis-cdn.deltavdevs.com/",
    ];

    public static bool IsTrustedFeedBase(string? feedBase)
    {
        if (string.IsNullOrWhiteSpace(feedBase) ||
            !Uri.TryCreate(feedBase, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        // Compare the normalized form, so "…/download/../../evil" can't ride in on the prefix.
        var normalized = uri.AbsoluteUri.TrimEnd('/') + "/";
        return TrustedFeedPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// The releases that have a build for <paramref name="rid"/>, newest first. Entries that are
    /// malformed, untrusted or for another platform are skipped instead of failing the whole list.
    /// </summary>
    public static IReadOnlyList<ReleaseInfo> Parse(string json, string rid)
    {
        var releases = new List<ReleaseInfo>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return releases;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var version = Text(item, "version");
                var feedBase = Text(item, "feedBase");
                if (!ReleaseVersion.TryParse(version, out _) || !IsTrustedFeedBase(feedBase))
                {
                    continue;
                }

                if (!item.TryGetProperty("platforms", out var platforms) ||
                    platforms.ValueKind != JsonValueKind.Object ||
                    !platforms.TryGetProperty(rid, out var platform) ||
                    platform.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var installer = Text(platform, "installer");
                var notes = Text(item, "notesUrl");
                releases.Add(new ReleaseInfo(
                    version,
                    string.Equals(Text(item, "channel"), "beta", StringComparison.OrdinalIgnoreCase),
                    Text(item, "date"),
                    feedBase.TrimEnd('/'),
                    notes.StartsWith("https://", StringComparison.Ordinal) ? notes : string.Empty,
                    installer.StartsWith("https://", StringComparison.Ordinal) ? installer : string.Empty));
            }
        }
        catch (JsonException)
        {
            return [];
        }

        releases.Sort(static (a, b) => ReleaseVersion.Compare(b.Version, a.Version));
        return releases;
    }

    public static ReleaseInfo? NewestBeta(IEnumerable<ReleaseInfo> releases) =>
        releases.Where(static r => r.IsBeta).MaxBy(static r => r.Version, ReleaseVersion.Comparer);

    /// <summary>The newest opted-in release, strictly newer than both the install and the CDN offer.</summary>
    public static ReleaseInfo? NewestUpdate(IEnumerable<ReleaseInfo> releases, string currentVersion,
        bool includeBetas, string? offeredVersion = null) =>
        releases.Where(r => (includeBetas || !r.IsBeta) &&
                            ReleaseVersion.Compare(r.Version, currentVersion) > 0 &&
                            (offeredVersion is null || ReleaseVersion.Compare(r.Version, offeredVersion) > 0))
            .MaxBy(static r => r.Version, ReleaseVersion.Comparer);

    public static async Task<IReadOnlyList<ReleaseInfo>> FetchAsync(string rid, CancellationToken ct)
    {
        using var http = Diagnostics.NetworkClients.Create("releases", TimeSpan.FromSeconds(15));
        var json = await http.GetStringAsync(Url, ct);
        return Parse(json, rid);
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;
}

/// <summary>
/// Semver ordering for app versions: <c>7.1.1-beta.4 &lt; 7.1.1-beta.10 &lt; 7.1.1 &lt; 7.2.0</c>.
/// <see cref="System.Version"/> can't parse a pre-release, so update checks compare through this.
/// </summary>
public static class ReleaseVersion
{
    public static IComparer<string> Comparer { get; } = Comparer<string>.Create(Compare);

    public static bool TryParse(string? text, out (int Major, int Minor, int Patch, string Pre) version)
    {
        version = default;
        var value = text?.Trim() ?? string.Empty;
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var plus = value.IndexOf('+');
        if (plus >= 0)
        {
            value = value[..plus];
        }

        var pre = string.Empty;
        var dash = value.IndexOf('-');
        if (dash >= 0)
        {
            pre = value[(dash + 1)..];
            value = value[..dash];
            if (pre.Length == 0 || !pre.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
            {
                return false;
            }
        }

        var parts = value.Split('.');
        // A fourth part ("7.1.0.0", what an assembly version looks like) is accepted and ignored.
        if (parts.Length is < 3 or > 4 ||
            !int.TryParse(parts[0], out var major) || major < 0 ||
            !int.TryParse(parts[1], out var minor) || minor < 0 ||
            !int.TryParse(parts[2], out var patch) || patch < 0)
        {
            return false;
        }

        version = (major, minor, patch, pre);
        return true;
    }

    public static bool IsPrerelease(string? text) => TryParse(text, out var v) && v.Pre.Length > 0;

    /// <summary>Negative when <paramref name="a"/> is older than <paramref name="b"/>. Unparseable input sorts lowest.</summary>
    public static int Compare(string? a, string? b)
    {
        var okA = TryParse(a, out var x);
        var okB = TryParse(b, out var y);
        if (!okA || !okB)
        {
            return okA.CompareTo(okB);
        }

        var core = x.Major != y.Major ? x.Major.CompareTo(y.Major)
            : x.Minor != y.Minor ? x.Minor.CompareTo(y.Minor)
            : x.Patch.CompareTo(y.Patch);
        if (core != 0)
        {
            return core;
        }

        if (x.Pre.Length == 0 || y.Pre.Length == 0)
        {
            // a release is newer than any of its pre-releases
            return x.Pre.Length == y.Pre.Length ? 0 : x.Pre.Length == 0 ? 1 : -1;
        }

        var xs = x.Pre.Split('.');
        var ys = y.Pre.Split('.');
        for (var i = 0; i < Math.Max(xs.Length, ys.Length); i++)
        {
            if (i >= xs.Length) return -1;
            if (i >= ys.Length) return 1;
            var xNumeric = int.TryParse(xs[i], out var xn);
            var yNumeric = int.TryParse(ys[i], out var yn);
            var part = xNumeric && yNumeric ? xn.CompareTo(yn)
                : xNumeric != yNumeric ? (xNumeric ? -1 : 1)
                : string.CompareOrdinal(xs[i], ys[i]);
            if (part != 0)
            {
                return part;
            }
        }

        return 0;
    }
}
