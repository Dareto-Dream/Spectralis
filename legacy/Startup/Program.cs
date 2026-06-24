using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using Squirrel;

namespace Spectralis;

internal static class Program
{
    private const string AppId = "Spectralis";
    private const string AppExecutableName = "Spectralis.exe";
    private const string LegacyAppExecutableName = "AudioPlayer.exe";
    private const ShortcutLocation DefaultShortcutLocations =
        ShortcutLocation.StartMenu | ShortcutLocation.Desktop;
    private const string UpdateFeedUrl = "https://cdn.deltavdevs.com/spectralis";

    private static string UpdateLogPath => AppLogPaths.For("updates.log");

    [STAThread]
    static void Main(string[] args)
    {
        AppDataMigration.MigrateLegacyFolder();

        SquirrelAwareApp.HandleEvents(
            onInitialInstall: _ => RunSquirrelTransition(
                "initial install",
                () => CompleteInstallTransition(updateOnly: false)),
            onAppUpdate: version =>
            {
                AppUpdateNoticeStore.MarkPending(version?.ToString());
                RunSquirrelTransition(
                    "app update",
                    () => CompleteInstallTransition(updateOnly: true));
            },
            onAppObsoleted: _ => { },
            onAppUninstall: _ => RunSquirrelTransition("app uninstall", CompleteUninstallTransition),
            onFirstRun: () => { },
            arguments: args);

        var filteredArgs = args
            .Where(static argument => !argument.StartsWith("--squirrel", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var startupPath = filteredArgs
            .Select(TryGetExistingFilePath)
            .FirstOrDefault(static path => path is not null);

        var startupExternalOpenRequest = filteredArgs
            .Select(TryGetExternalOpenRequest)
            .FirstOrDefault(static request => request is not null);

        var startupSharedPlayJoin = filteredArgs
            .Select(TryGetSharedPlayJoinRequest)
            .FirstOrDefault(static request => request is not null);

        var externalOpenRequest = CreateExternalOpenRequest(
            startupPath,
            startupExternalOpenRequest,
            startupSharedPlayJoin);
        if (externalOpenRequest is not null &&
            PreserveSessionStartupSettings.Load().PreserveSession &&
            ExternalOpenIpc.TrySendAsync(externalOpenRequest).GetAwaiter().GetResult())
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        if (!ShowExternalApiConsentIfNeeded())
            return;

        RegisterWindowsIntegrationIfNeeded();

        var mainForm = new Form1(startupPath, startupSharedPlayJoin, startupExternalOpenRequest);
        var updateCheckStarted = false;
        var updateCheckInProgress = false;
        async Task RunUpdateCheckAsync(bool manual)
        {
            if (updateCheckInProgress)
            {
                if (manual)
                {
                    MessageBox.Show(
                        mainForm,
                        "Spectralis is already checking for updates.",
                        "Update Check",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            try
            {
                updateCheckInProgress = true;
                await CheckForUpdatesAsync(mainForm, manual);
            }
            finally
            {
                updateCheckInProgress = false;
            }
        }

        mainForm.ManualUpdateCheckRequested += (_, _) => _ = RunUpdateCheckAsync(manual: true);
        mainForm.Shown += (_, _) =>
        {
            if (updateCheckStarted)
                return;

            updateCheckStarted = true;
            _ = RunUpdateCheckAsync(manual: false);
        };

        Application.Run(mainForm);
    }

    private static void WithUpdateManager(Action<UpdateManager> action)
    {
        using var manager = new UpdateManager(UpdateFeedUrl, AppId, null, null);
        action(manager);
    }

    private static void RunSquirrelTransition(string transitionName, Action action)
    {
        try
        {
            LogUpdate($"Running Squirrel {transitionName} transition.");
            action();
            LogUpdate($"Completed Squirrel {transitionName} transition.");
        }
        catch (Exception ex)
        {
            LogUpdate($"Squirrel {transitionName} transition failed: {ex}");
            Debug.WriteLine($"Squirrel {transitionName} transition failed: {ex}");
        }
    }

    private static void CompleteInstallTransition(bool updateOnly)
    {
        TryRunIntegrationStep("Shortcut transition", () =>
            WithUpdateManager(manager =>
            {
                if (updateOnly)
                {
                    UpdateOrMigrateShortcuts(manager);
                    return;
                }

                manager.CreateShortcutsForExecutable(
                    AppExecutableName,
                    DefaultShortcutLocations,
                    updateOnly: false);
            }));

        TryRunIntegrationStep(
            "Default app registration",
            DefaultAppRegistrar.RegisterCurrentUser);
    }

    private static void CompleteUninstallTransition()
    {
        TryRunIntegrationStep("Shortcut cleanup", () =>
            WithUpdateManager(manager =>
            {
                manager.RemoveShortcutsForExecutable(AppExecutableName, DefaultShortcutLocations);
                manager.RemoveShortcutsForExecutable(LegacyAppExecutableName, DefaultShortcutLocations);
            }));
    }

    private static void UpdateOrMigrateShortcuts(UpdateManager manager)
    {
        var currentShortcutLocations = GetShortcutLocations(manager, AppExecutableName);
        var legacyShortcutLocations = GetShortcutLocations(manager, LegacyAppExecutableName);
        var locationsToCreate = legacyShortcutLocations & ~currentShortcutLocations;

        if (locationsToCreate != 0)
        {
            LogUpdate(
                $"Migrating shortcuts from {LegacyAppExecutableName} to {AppExecutableName}: {locationsToCreate}.");
            manager.CreateShortcutsForExecutable(
                AppExecutableName,
                locationsToCreate,
                updateOnly: false);
        }

        manager.CreateShortcutsForExecutable(
            AppExecutableName,
            DefaultShortcutLocations,
            updateOnly: true);

        if (legacyShortcutLocations != 0)
            manager.RemoveShortcutsForExecutable(LegacyAppExecutableName, legacyShortcutLocations);
    }

    private static ShortcutLocation GetShortcutLocations(UpdateManager manager, string executableName)
    {
        try
        {
            var shortcuts = manager.GetShortcutsForExecutable(
                executableName,
                DefaultShortcutLocations,
                programArguments: null);

            var locations = (ShortcutLocation)0;
            foreach (var location in shortcuts.Keys)
            {
                locations |= location;
            }

            return locations;
        }
        catch (Exception ex)
        {
            LogUpdate($"Could not inspect shortcuts for {executableName}: {ex}");
            return 0;
        }
    }

    private static void TryRunIntegrationStep(string stepName, Action action)
    {
        try
        {
            action();
            LogUpdate($"{stepName} completed.");
        }
        catch (Exception ex)
        {
            LogUpdate($"{stepName} failed: {ex}");
            Debug.WriteLine($"{stepName} failed: {ex}");
        }
    }

    private static async Task CheckForUpdatesAsync(Form owner, bool manual = false)
    {
        try
        {
            LogUpdate(manual ? "Checking for updates manually." : "Checking for updates.");
            using var manager = new UpdateManager(UpdateFeedUrl, AppId, null, null);
            if (!manager.IsInstalledApp)
            {
                LogUpdate("Update check skipped because this is not a Squirrel-installed app.");
                if (manual)
                {
                    MessageBox.Show(
                        owner,
                        "Updates are only available in the installed version of Spectralis.",
                        "Update Check",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            var updateInfo = await manager.CheckForUpdate(ignoreDeltaUpdates: true);
            if (updateInfo.ReleasesToApply.Count == 0)
            {
                LogUpdate("No updates available.");
                if (manual)
                    ShowNoUpdatesMessage(owner);

                return;
            }

            var updateVersion = GetUpdateVersion(updateInfo);
            LogUpdate($"Update available: {updateVersion}. Releases: {FormatReleaseList(updateInfo.ReleasesToApply)}");

            // If the running exe is already the target version, Squirrel's local
            // RELEASES tracking may be stale but the update was already applied.
            if (IsAlreadyOnTargetVersion(updateVersion))
            {
                LogUpdate($"Update skipped because the running version already matches {updateVersion}.");
                if (manual)
                    ShowNoUpdatesMessage(owner);

                return;
            }

            var settings = UpdateStartupSettings.Load();
            if (settings.EnableAutoUpdates && !manual)
            {
                await DownloadApplyAndNotifyAsync(manager, updateInfo, owner, updateVersion);
                return;
            }

            if (!manual &&
                !string.IsNullOrWhiteSpace(updateVersion) &&
                string.Equals(settings.IgnoredUpdateVersion, updateVersion, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            using var prompt = new UpdatePromptDialog(updateVersion);
            prompt.ShowDialog(owner);

            switch (prompt.Choice)
            {
                case UpdatePromptChoice.UpdateNow:
                    await DownloadApplyAndNotifyAsync(manager, updateInfo, owner, updateVersion);
                    break;
                case UpdatePromptChoice.DontRemindAgain:
                    if (!string.IsNullOrWhiteSpace(updateVersion))
                        UpdateStartupSettings.SaveIgnoredUpdateVersion(updateVersion);
                    break;
            }
        }
        catch (Exception ex)
        {
            LogUpdate($"Update check failed: {ex}");
            Debug.WriteLine($"Squirrel update check failed: {ex}");

            if (manual)
            {
                MessageBox.Show(
                    owner,
                    $"Spectralis couldn't check for updates.{Environment.NewLine}{Environment.NewLine}Log: {UpdateLogPath}",
                    "Update Check Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }

    private static async Task DownloadApplyAndNotifyAsync(
        UpdateManager manager,
        UpdateInfo updateInfo,
        Form owner,
        string? updateVersion = null)
    {
        UpdateProgressDialog? progressDialog = null;
        var ownerWasEnabled = owner.Enabled;
        try
        {
            LogUpdate($"Starting update install for {updateVersion ?? "unknown version"}.");
            owner.UseWaitCursor = true;
            owner.Enabled = false;
            progressDialog = new UpdateProgressDialog(updateVersion);
            progressDialog.UseWaitCursor = true;
            progressDialog.Show(owner);
            progressDialog.SetStatus("Downloading update...");

            LogUpdate($"Downloading releases: {FormatReleaseList(updateInfo.ReleasesToApply)}");
            await manager.DownloadReleases(updateInfo.ReleasesToApply);
            LogUpdate("Download completed.");

            progressDialog.SetStatus("Installing update...");
            LogUpdate("Applying releases.");
            await manager.ApplyReleases(updateInfo);
            LogUpdate("Apply releases completed.");

            progressDialog.SetStatus("Update installed.");

            if (progressDialog is not null && !progressDialog.IsDisposed)
            {
                progressDialog.Close();
                progressDialog = null;
            }

            owner.Enabled = ownerWasEnabled;
            owner.UseWaitCursor = false;

            var installedMessage = string.IsNullOrWhiteSpace(updateVersion)
                ? "The update has finished installing. To use the new update, close all instances of Spectralis and start the app again."
                : $"Spectralis {updateVersion.Trim()} has finished installing. To use the new update, close all instances of Spectralis and start the app again.";

            MessageBox.Show(
                owner,
                installedMessage,
                "Spectralis Update Installed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            LogUpdate($"Update install failed: {ex}");
            Debug.WriteLine($"Squirrel update install failed: {ex}");

            MessageBox.Show(
                owner,
                $"Spectralis couldn't finish installing the update. Please try again, or install the latest Setup.exe manually.{Environment.NewLine}{Environment.NewLine}Log: {UpdateLogPath}",
                "Spectralis Update Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (progressDialog is not null && !progressDialog.IsDisposed)
                progressDialog.Close();

            if (!owner.IsDisposed)
            {
                owner.Enabled = ownerWasEnabled;
                owner.UseWaitCursor = false;
            }
        }
    }

    private static string GetUpdateVersion(UpdateInfo updateInfo) =>
        updateInfo.FutureReleaseEntry?.Version?.ToString()
        ?? updateInfo.ReleasesToApply
            .OrderBy(static release => release.Version)
            .LastOrDefault()
            ?.Version
            ?.ToString()
        ?? "";

    private static string FormatReleaseList(IEnumerable<ReleaseEntry> releases) =>
        string.Join(", ", releases.Select(static release => release.Filename));

    private static void ShowNoUpdatesMessage(Form owner)
    {
        MessageBox.Show(
            owner,
            "You're running the latest version of Spectralis.",
            "Update Check",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static void LogUpdate(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(UpdateLogPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.AppendAllText(
                UpdateLogPath,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never interfere with update checks or installs.
        }
    }

    private static bool IsAlreadyOnTargetVersion(string? targetVersion)
    {
        if (string.IsNullOrWhiteSpace(targetVersion))
            return false;

        if (!Version.TryParse(targetVersion.TrimStart('v'), out var target))
            return false;

        var assembly = Assembly.GetEntryAssembly() ?? typeof(Program).Assembly;
        var infoVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var rawVersion = string.IsNullOrWhiteSpace(infoVersion)
            ? assembly.GetName().Version?.ToString()
            : infoVersion.Split('+')[0].Trim();

        if (!Version.TryParse(rawVersion?.TrimStart('v'), out var running))
            return false;

        return target.Major == running.Major
            && target.Minor == running.Minor
            && Math.Max(0, target.Build) == Math.Max(0, running.Build);
    }

    private static void RegisterWindowsIntegrationIfNeeded()
    {
        try
        {
            if (DefaultAppRegistrar.ShouldRefreshCurrentUserRegistration())
            {
                LogUpdate("Refreshing Windows integration registration.");
                DefaultAppRegistrar.RegisterCurrentUser();
                return;
            }

            if (!DefaultAppRegistrar.IsSharedPlayProtocolRegisteredForCurrentUser())
            {
                LogUpdate("Registering Shared Play protocol.");
                DefaultAppRegistrar.RegisterSharedPlayProtocolCurrentUser();
            }
        }
        catch (Exception ex)
        {
            LogUpdate($"Windows integration registration failed: {ex}");
            Debug.WriteLine($"Windows integration registration failed: {ex}");
        }
    }

    private static string? TryGetExistingFilePath(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
            return null;

        try
        {
            var candidatePath = Path.GetFullPath(argument.Trim('"'));
            return File.Exists(candidatePath) ? candidatePath : null;
        }
        catch
        {
            return null;
        }
    }

    private static SharedPlayJoinRequest? TryGetSharedPlayJoinRequest(string argument) =>
        SharedPlayJoinRequest.TryParse(argument, allowRawSessionId: false, out var request)
            ? request
            : null;

    private static ExternalOpenRequest? CreateExternalOpenRequest(
        string? startupPath,
        ExternalOpenRequest? startupExternalOpenRequest,
        SharedPlayJoinRequest? startupSharedPlayJoin)
    {
        if (startupSharedPlayJoin is not null)
            return new ExternalOpenRequest(
                ExternalOpenKind.SharedPlay,
                startupSharedPlayJoin.SessionId,
                startupSharedPlayJoin.CdnBaseUrl);

        if (startupExternalOpenRequest is not null)
            return startupExternalOpenRequest;

        return !string.IsNullOrWhiteSpace(startupPath)
            ? new ExternalOpenRequest(ExternalOpenKind.File, startupPath)
            : null;
    }

    private static ExternalOpenRequest? TryGetExternalOpenRequest(string argument)
    {
        var value = argument?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "spectralis", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var pathParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(uri.Host))
            pathParts.Add(uri.Host);

        pathParts.AddRange(uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (pathParts.Count == 0 ||
            !string.Equals(pathParts[0], "open", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url))
            return null;

        return new ExternalOpenRequest(
            ExternalOpenKind.Url,
            url.Trim(),
            Intent: ParseExternalOpenIntent(query));
    }

    private static ExternalOpenIntent ParseExternalOpenIntent(IReadOnlyDictionary<string, string> query)
    {
        if (query.TryGetValue("queue", out var queueValue) &&
            IsTruthyQueryValue(queueValue))
        {
            return ExternalOpenIntent.QueueNext;
        }

        var rawIntent =
            GetQueryValue(query, "intent") ??
            GetQueryValue(query, "mode") ??
            GetQueryValue(query, "action");

        return rawIntent?.Trim().ToLowerInvariant() switch
        {
            "play" or "play-now" or "open" or "replace" => ExternalOpenIntent.PlayNow,
            "queue" or "queue-next" or "next" or "add-next" => ExternalOpenIntent.QueueNext,
            "queue-end" or "queue-last" or "end" or "append" or "add-end" => ExternalOpenIntent.QueueEnd,
            _ => ExternalOpenIntent.Default
        };
    }

    private static string? GetQueryValue(IReadOnlyDictionary<string, string> query, string key) =>
        query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static bool IsTruthyQueryValue(string value) =>
        value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var value = parts.Length > 1
                ? Uri.UnescapeDataString(parts[1].Replace('+', ' '))
                : "";
            values[key] = value;
        }

        return values;
    }

    private static bool ShowExternalApiConsentIfNeeded()
    {
        if (ExternalApiConsentStartupSettings.Load().ExternalApiConsentAccepted)
            return true;

        if (!ExternalApiConsentDialog.ConfirmProceed())
            return false;

        ExternalApiConsentStartupSettings.SaveAccepted();
        return true;
    }

    private sealed class ExternalApiConsentStartupSettings
    {
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true
        };

        private static readonly JsonDocumentOptions ReadOptions = new()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        private static string SettingsPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spectralis",
                "settings.json");

        public bool ExternalApiConsentAccepted { get; init; }

        public static ExternalApiConsentStartupSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new ExternalApiConsentStartupSettings();

                var json = File.ReadAllText(SettingsPath);
                var root = JsonNode.Parse(json, documentOptions: ReadOptions) as JsonObject;
                if (root is null)
                    return new ExternalApiConsentStartupSettings();

                return new ExternalApiConsentStartupSettings
                {
                    ExternalApiConsentAccepted = ReadBoolean(root, "ExternalApiConsentAccepted")
                };
            }
            catch
            {
                return new ExternalApiConsentStartupSettings();
            }
        }

        public static void SaveAccepted()
        {
            try
            {
                JsonObject root;
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    root = JsonNode.Parse(json, documentOptions: ReadOptions) as JsonObject ?? [];
                }
                else
                {
                    root = [];
                }

                root["ExternalApiConsentAccepted"] = true;

                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(SettingsPath, root.ToJsonString(WriteOptions));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unable to save external API consent: {ex}");
            }
        }

        private static bool ReadBoolean(JsonObject root, string propertyName)
        {
            try
            {
                return root[propertyName]?.GetValue<bool>() ?? false;
            }
            catch
            {
                return false;
            }
        }
    }

    private sealed class UpdateStartupSettings
    {
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true
        };

        private static readonly JsonDocumentOptions ReadOptions = new()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        private static string SettingsPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spectralis",
                "settings.json");

        public bool EnableAutoUpdates { get; init; }
        public string IgnoredUpdateVersion { get; init; } = "";

        public static UpdateStartupSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new UpdateStartupSettings();

                var json = File.ReadAllText(SettingsPath);
                var root = JsonNode.Parse(json, documentOptions: ReadOptions) as JsonObject;
                if (root is null)
                    return new UpdateStartupSettings();

                return new UpdateStartupSettings
                {
                    EnableAutoUpdates = ReadBoolean(root, "EnableAutoUpdates"),
                    IgnoredUpdateVersion = ReadString(root, "IgnoredUpdateVersion")
                };
            }
            catch
            {
                return new UpdateStartupSettings();
            }
        }

        public static void SaveIgnoredUpdateVersion(string version)
        {
            try
            {
                JsonObject root;
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    root = JsonNode.Parse(json, documentOptions: ReadOptions) as JsonObject ?? [];
                }
                else
                {
                    root = [];
                }

                root["IgnoredUpdateVersion"] = version.Trim();

                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(SettingsPath, root.ToJsonString(WriteOptions));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unable to save ignored update version: {ex}");
            }
        }

        private static bool ReadBoolean(JsonObject root, string propertyName)
        {
            try
            {
                return root[propertyName]?.GetValue<bool>() ?? false;
            }
            catch
            {
                return false;
            }
        }

        private static string ReadString(JsonObject root, string propertyName)
        {
            try
            {
                return root[propertyName]?.GetValue<string>()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }
    }

    private sealed class PreserveSessionStartupSettings
    {
        private static readonly JsonDocumentOptions ReadOptions = new()
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        private static string SettingsPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spectralis",
                "settings.json");

        public bool PreserveSession { get; init; } = true;

        public static PreserveSessionStartupSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new PreserveSessionStartupSettings();

                var json = File.ReadAllText(SettingsPath);
                var root = JsonNode.Parse(json, documentOptions: ReadOptions) as JsonObject;
                if (root is null)
                    return new PreserveSessionStartupSettings();

                return new PreserveSessionStartupSettings
                {
                    PreserveSession = ReadBoolean(root, "PreserveSession", fallback: true)
                };
            }
            catch
            {
                return new PreserveSessionStartupSettings();
            }
        }

        private static bool ReadBoolean(JsonObject root, string propertyName, bool fallback)
        {
            try
            {
                return root[propertyName]?.GetValue<bool>() ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
