namespace Spectralis.Core.Platform;

/// <summary>
/// Where the app talks to its own hosted service. Warnings, the changelog, verified creators and
/// Shared Play all live on one self-hosted backend; nothing is read as a file off a CDN any more.
/// </summary>
public static class SpectralisEndpoints
{
    public const string ApiBase = "https://spectralis-api.deltavdevs.com";

    public const string WarningsPath = "/spectralis/v1/warnings";
    public const string ChangelogPath = "/spectralis/v1/changelog";
    public const string CommunityPath = "/spectralis/v1/community";

    /// <summary>Relative to <see cref="ApiBase"/> with a trailing slash. {0} is the key fingerprint.</summary>
    public const string CreatorPathTemplate = "spectralis/v1/creators/{0}";

    public const string WarningsUrl = ApiBase + WarningsPath;
    public const string ChangelogUrl = ApiBase + ChangelogPath;
    public const string CommunityUrl = ApiBase + CommunityPath;
}
