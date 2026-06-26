using System.Text.RegularExpressions;
using Spectralis.Core.Platform;
using Velopack;
using Velopack.Sources;

namespace Spectralis.App.Services;

/// <summary>
/// Implements IUpdateService using Velopack for in-process download and apply.
/// Falls back to a direct HTTP parse of the Velopack feed JSON when not running
/// inside a Velopack-managed installation (e.g. Squirrel-migrated installs).
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    // TODO 5.1.0: select channel by platform (win-x64 / linux-x64 / osx-x64|arm64)
    // once non-Windows Velopack builds are produced by build-velopack.ps1.
    private const string ReleasesUrl = "https://cdn.deltavdevs.com/spectralis";
    private const string Channel = "win-x64";

    public static string UpdateLogPath => AppLogPaths.For("updates.log");

    public string CurrentVersion =>
        DiagnosticsSnapshot.CurrentVersion ?? "0.0.0";

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct)
    {
        LogUpdate("Checking for updates.");
        try
        {
            var mgr = CreateManager();
            var update = await Task.Run(() => mgr.CheckForUpdatesAsync(), ct);
            if (update is null)
            {
                LogUpdate("No updates available (Velopack SDK).");
                return new UpdateCheckResult(false, null, null) { SupportsInProcessUpdate = true };
            }

            var version = update.TargetFullRelease.Version.ToString();
            LogUpdate($"Update available (Velopack SDK): {version}");
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

    private async Task<UpdateCheckResult> CheckViaHttpFallbackAsync(CancellationToken ct)
    {
        try
        {
            var feedUrl = $"{ReleasesUrl}/releases.{Channel}.json";
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var json = await http.GetStringAsync(feedUrl, ct);

            // Scan for version strings in Velopack nupkg filenames:
            // "Spectralis-5.0.3-win-x64-full.nupkg"
            var latestVersion = Regex
                .Matches(json, $@"Spectralis-(\d+\.\d+(?:\.\d+){{0,2}})-{Channel}", RegexOptions.IgnoreCase)
                .Select(m => Version.TryParse(m.Groups[1].Value, out var v) ? v : null)
                .Where(v => v is not null)
                .Max();

            if (latestVersion is null)
            {
                LogUpdate("HTTP feed check: no version found in feed JSON.");
                return new UpdateCheckResult(false, null, null);
            }

            var latestStr = $"{latestVersion.Major}.{latestVersion.Minor}.{latestVersion.Build}";

            if (!Version.TryParse(CurrentVersion, out var current) || latestVersion > current)
            {
                LogUpdate($"Update available (HTTP feed): {latestStr}");
                return new UpdateCheckResult(true, latestStr, null);
            }

            LogUpdate($"No updates available (HTTP feed, latest is {latestStr}).");
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
        var mgr = CreateManager();
        var update = await Task.Run(() => mgr.CheckForUpdatesAsync(), ct);
        if (update is null)
        {
            LogUpdate("No update found during download check.");
            return;
        }

        LogUpdate($"Downloading update: {update.TargetFullRelease.Version}");
        await mgr.DownloadUpdatesAsync(update, progress == null ? null : p => progress.Report(p / 100.0));
        LogUpdate("Download complete. Applying and restarting.");
        mgr.ApplyUpdatesAndRestart(update);
    }

    public void RestartToUpdate()
    {
        LogUpdate("Restarting to apply update.");
        var mgr = CreateManager();
        mgr.ApplyUpdatesAndRestart(null);
    }

    private static UpdateManager CreateManager() =>
        new(new SimpleWebSource(ReleasesUrl));

    private static void LogUpdate(string message)
    {
        try { AppLogPaths.AppendTimestamped(UpdateLogPath, message); }
        catch { }
    }
}
