using System.Text.RegularExpressions;
using Spectralis.Core.Platform;
using Velopack;
using Velopack.Sources;

namespace Spectralis.App.Services;

/// <summary>
/// Implements IUpdateService using Velopack for in-process download and apply.
/// Falls back to a direct HTTP parse of the Velopack feed JSON when not running
/// inside a Velopack-managed installation (e.g. Squirrel-migrated installs).
///
/// Three things can be installed through it:
///  - the newest stable release, from the release CDN (everyone, by default);
///  - the newest beta, for people who picked the Beta channel in Settings. Betas live on GitHub
///    Releases and are found through the release index; a beta user still moves to a stable
///    release as soon as it is newer than their beta;
///  - any specific version from the release index, older ones included (see
///    <see cref="InstallVersionAsync"/>).
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    // Cloudflare R2 behind the spectralis-cdn subdomain. Builds before this one still read the legacy
    // cdn.deltavdevs.com/spectralis feed, which deploy.ps1 keeps publishing to.
    private const string ReleasesUrl = "https://spectralis-cdn.deltavdevs.com";

    public const string StableChannel = "stable";
    public const string BetaChannel = "beta";

    /// <summary>The platform's feed name: every release publishes <c>releases.&lt;rid&gt;.json</c>.</summary>
    public static string Channel =>
        OperatingSystem.IsWindows() ? "win-x64" :
        OperatingSystem.IsLinux()   ? "linux-x64" :
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
            System.Runtime.InteropServices.Architecture.Arm64
            ? "osx-arm64" : "osx-x64";

    public static string UpdateLogPath => AppLogPaths.For("updates.log");

    private readonly bool _followBeta;

    public VelopackUpdateService()
        : this(IsBetaChannel(AppSettingsStore.Load().UpdateChannel))
    {
    }

    public VelopackUpdateService(bool followBeta) => _followBeta = followBeta;

    public static bool IsBetaChannel(string? channel) =>
        string.Equals(channel?.Trim(), BetaChannel, StringComparison.OrdinalIgnoreCase);

    public string CurrentVersion =>
        DiagnosticsSnapshot.CurrentVersion ?? "0.0.0";

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct)
    {
        LogUpdate($"Checking for updates ({(_followBeta ? BetaChannel : StableChannel)} channel).");
        try
        {
            var plan = await ResolveAsync(ct);
            if (plan is null)
            {
                LogUpdate("No updates available (Velopack SDK).");
                return new UpdateCheckResult(false, null, null) { SupportsInProcessUpdate = true };
            }

            var version = plan.Update.TargetFullRelease.Version.ToString();
            LogUpdate($"Update available (Velopack SDK): {version} from {plan.Source}");
            return new UpdateCheckResult(true, version, null) { SupportsInProcessUpdate = true };
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(false, null, null);
        }
        catch (Exception sdkEx)
        {
            LogUpdate($"Velopack SDK unavailable ({sdkEx.Message}); falling back to HTTP feed check.");
            return await CheckViaHttpFallbackAsync(ct);
        }
    }

    private sealed record UpdatePlan(UpdateManager Manager, UpdateInfo Update, string Source);

    /// <summary>
    /// Finds what to update to: the stable feed's newest release, or the newest beta when the user
    /// follows betas and it is newer than both what is installed and what stable offers.
    /// </summary>
    private async Task<UpdatePlan?> ResolveAsync(CancellationToken ct)
    {
        var stableManager = new UpdateManager(new SimpleWebSource(ReleasesUrl));
        UpdateInfo? stable = null;
        Exception? stableError = null;
        try
        {
            stable = await Spectralis.Core.Diagnostics.NetworkLog.TrackAsync(
                "updater", "CHECK", $"{ReleasesUrl}/releases.{Channel}.json",
                () => Task.Run(() => stableManager.CheckForUpdatesAsync(), ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stableError = ex;
            LogUpdate($"Stable CDN check failed: {ex.Message}");
        }
        var best = stable is null ? null : new UpdatePlan(stableManager, stable, ReleasesUrl);

        try
        {
            // GitHub remains usable when the CDN is down. Stable users can also recover a stable
            // update through the archive; betas are considered only after opting in.
            if (!_followBeta && stableError is null) return best;
            var release = ReleaseIndex.NewestUpdate(await ReleaseIndex.FetchAsync(Channel, ct),
                CurrentVersion, _followBeta, best?.Update.TargetFullRelease.Version.ToString());
            if (release is not null)
            {
                var manager = new UpdateManager(new SimpleWebSource(release.FeedBase));
                var update = await Spectralis.Core.Diagnostics.NetworkLog.TrackAsync(
                    "updater", "CHECK", $"{release.FeedBase}/releases.{Channel}.json",
                    () => Task.Run(() => manager.CheckForUpdatesAsync(), ct));
                if (update is not null) return new UpdatePlan(manager, update, release.FeedBase);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The beta lookup is extra; a stable update must still get through without it.
            LogUpdate($"Release index check skipped: {ex.Message}");
        }

        if (best is null && stableError is not null) throw stableError;
        return best;
    }

    private async Task<UpdateCheckResult> CheckViaHttpFallbackAsync(CancellationToken ct)
    {
        try
        {
            var feedUrl = $"{ReleasesUrl}/releases.{Channel}.json";
            using var http = Spectralis.Core.Diagnostics.NetworkClients.Create("updater", TimeSpan.FromSeconds(10));
            string json = string.Empty;
            var cdnFailed = false;
            try
            {
                json = await http.GetStringAsync(feedUrl, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                cdnFailed = true;
                LogUpdate($"HTTP CDN check failed: {ex.Message}");
            }

            // Scan for version strings in Velopack nupkg filenames:
            // "Spectralis-5.0.3-win-x64-full.nupkg"
            var latest = Regex
                .Matches(json, $@"Spectralis-(\d+\.\d+\.\d+)-{Channel}", RegexOptions.IgnoreCase)
                .Select(m => m.Groups[1].Value)
                .MaxBy(v => v, ReleaseVersion.Comparer);

            try
            {
                if (_followBeta || cdnFailed)
                {
                    var indexed = ReleaseIndex.NewestUpdate(await ReleaseIndex.FetchAsync(Channel, ct),
                        CurrentVersion, includeBetas: _followBeta, offeredVersion: latest);
                    latest = indexed?.Version ?? latest;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogUpdate($"HTTP release index check skipped: {ex.Message}");
            }

            // Compared as semver so a beta build (7.1.1-beta.3) is not told that 7.1.0 is an update.
            if (latest is not null && ReleaseVersion.Compare(latest, CurrentVersion) > 0)
            {
                LogUpdate($"Update available (HTTP feed): {latest}");
                return new UpdateCheckResult(true, latest, null);
            }

            LogUpdate($"No updates available (HTTP feed, latest is {latest}).");
            return new UpdateCheckResult(false, null, null);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(false, null, null);
        }
        catch (Exception httpEx)
        {
            LogUpdate($"HTTP feed check failed: {httpEx.Message}");
            return new UpdateCheckResult(false, null, null);
        }
    }

    public async Task DownloadAndApplyAsync(IProgress<double>? progress, CancellationToken ct)
    {
        LogUpdate("Starting update download and apply.");
        var plan = await ResolveAsync(ct);
        if (plan is null)
        {
            LogUpdate("No update found during download check.");
            return;
        }

        await DownloadAndRestartAsync(plan, progress, ct);
    }

    /// <summary>
    /// Installs one specific release from the index, whether it is newer or older than what is
    /// running, and restarts into it. Velopack only steps down a version when told it may.
    /// </summary>
    public async Task InstallVersionAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct)
    {
        if (!ReleaseIndex.IsTrustedFeedBase(release.FeedBase))
        {
            throw new InvalidOperationException("That release does not come from a Spectralis download location.");
        }

        if (ReleaseVersion.Compare(release.Version, CurrentVersion) == 0)
        {
            throw new InvalidOperationException($"Spectralis {release.Version} is already installed.");
        }

        LogUpdate($"Installing chosen version {release.Version} from {release.FeedBase}.");
        var manager = new UpdateManager(
            new SimpleWebSource(release.FeedBase),
            new UpdateOptions { AllowVersionDowngrade = true });
        var update = await Spectralis.Core.Diagnostics.NetworkLog.TrackAsync(
            "updater", "CHECK", $"{release.FeedBase}/releases.{Channel}.json",
            () => Task.Run(() => manager.CheckForUpdatesAsync(), ct));
        if (update is null)
        {
            throw new InvalidOperationException($"The download for {release.Version} isn't available for this platform.");
        }

        // The feed at a release's own address should end at that release. If it doesn't, stop:
        // installing "whatever the feed says" is not what was asked for.
        var offered = update.TargetFullRelease.Version.ToString();
        if (ReleaseVersion.Compare(offered, release.Version) != 0)
        {
            throw new InvalidOperationException($"Asked for {release.Version} but the download offers {offered}.");
        }

        await DownloadAndRestartAsync(new UpdatePlan(manager, update, release.FeedBase), progress, ct);
    }

    private static async Task DownloadAndRestartAsync(UpdatePlan plan, IProgress<double>? progress, CancellationToken ct)
    {
        var target = plan.Update.TargetFullRelease;
        LogUpdate($"Downloading update: {target.Version}");
        await Spectralis.Core.Diagnostics.NetworkLog.TrackAsync(
            "updater", "DOWNLOAD", $"{plan.Source}/{target.FileName}",
            () => plan.Manager.DownloadUpdatesAsync(plan.Update, progress == null ? null : p => progress.Report(p / 100.0), false, ct));
        LogUpdate("Download complete. Applying and restarting.");
        plan.Manager.ApplyUpdatesAndRestart(plan.Update);
    }

    public void RestartToUpdate()
    {
        LogUpdate("Restarting to apply update.");
        var mgr = new UpdateManager(new SimpleWebSource(ReleasesUrl));
        mgr.ApplyUpdatesAndRestart(null);
    }

    private static void LogUpdate(string message)
    {
        try { AppLogPaths.AppendTimestamped(UpdateLogPath, message); }
        catch { }
    }
}
