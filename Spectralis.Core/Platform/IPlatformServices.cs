namespace Spectralis.Core.Platform;

/// <summary>Registers spectralis:// protocol and audio-extension default-app associations.</summary>
public interface IProtocolRegistrar
{
    bool IsProtocolRegistered();
    void RegisterProtocol();
    bool AreFileAssociationsRegistered();
    void RegisterFileAssociations(IReadOnlyList<string> extensions);
}

/// <summary>System tray / status item with media controls.</summary>
public interface ITrayService : IDisposable
{
    void Show(string tooltip);
    void UpdateNowPlaying(string title, string artist);
    void Hide();
    event EventHandler? PlayPauseRequested;
    event EventHandler? NextRequested;
    event EventHandler? PreviousRequested;
    event EventHandler? OpenRequested;
    event EventHandler? ExitRequested;
}

/// <summary>
/// OS media session integration: SMTC on Windows, MPRIS on Linux, Now Playing on macOS.
/// </summary>
public interface IMediaSessionService : IDisposable
{
    void UpdateMetadata(string title, string artist, string album, byte[]? artwork);
    void UpdatePlaybackState(bool isPlaying, TimeSpan position, TimeSpan duration);
    event EventHandler? PlayRequested;
    event EventHandler? PauseRequested;
    event EventHandler? NextRequested;
    event EventHandler? PreviousRequested;
    event EventHandler<TimeSpan>? SeekRequested;
}

/// <summary>In-app updater (Squirrel on Windows; platform equivalents elsewhere).</summary>
public interface IUpdateService
{
    string CurrentVersion { get; }
    Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct);
    Task DownloadAndApplyAsync(IProgress<double>? progress, CancellationToken ct);
    void RestartToUpdate();
}

public sealed record UpdateCheckResult(bool UpdateAvailable, string? LatestVersion, string? ChangelogUrl)
{
    /// <summary>
    /// True when the Velopack SDK confirmed a managed install and can do an
    /// in-process download + restart. False when the version was detected via
    /// the HTTP feed fallback (Squirrel-migrated installs, dev builds, etc.) —
    /// in that case callers should open the download page instead.
    /// </summary>
    public bool SupportsInProcessUpdate { get; init; }
}
