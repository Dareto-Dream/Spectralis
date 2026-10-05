namespace Spectralis.Core.Platform;

/// <summary>
/// What an update check found. Velopack does the real checking (see VelopackUpdateService); this is the
/// shape the Settings screen and the startup notice share.
/// </summary>
public sealed record ReleaseFeedResult(bool IsUpdateAvailable, string? LatestVersion, string? DownloadUrl, string? ErrorMessage);
