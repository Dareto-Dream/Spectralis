using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.Core.Platform;

namespace Spectralis.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void OnChooseDefaultVisualizer(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel settings || TopLevel.GetTopLevel(this) is not Window owner) return;
        var selected = await VisualizerPickerWindow.ChooseAsync(owner,
            settings.VisualizerOptions.Where(o => o.Script is null && o.Installed is null),
            settings.SelectedDefaultVisualizer, settings.VisualizerPlayer, "Set app default");
        if (selected is not null) settings.SelectedDefaultVisualizer = selected;
    }

    private void OnRefreshOutputDevices(object? sender, RoutedEventArgs e) =>
        (DataContext as SettingsViewModel)?.RefreshOutputDevices();

    private async void OnLibraryAddFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel { Library: { } library } ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "Add watched library folder",
                AllowMultiple = true,
            });

        var paths = folders
            .Select(folder => folder.TryGetLocalPath())
            .Where(path => path is not null)
            .Select(path => path!)
            .Where(library.AddWatchedFolder)
            .ToList();

        if (paths.Count > 0)
        {
            await library.ScanPathsAsync(paths);
        }
    }

    private void OnLibraryRemoveFolder(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel { Library: { } library } &&
            WatchedFoldersList.SelectedItem is string folder)
        {
            library.RemoveWatchedFolder(folder);
        }
    }

    private async void OnLibraryRescan(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel { Library: { } library })
        {
            await library.RescanAsync();
        }
    }

    private void OnOpenDevTools(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        ShowUtilityWindow(new DevToolsWindow { DataContext = vm });
    }

    private async void OnCheckForUpdates(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var owner = TopLevel.GetTopLevel(this) as Window;

        vm.CheckForUpdates();
        var statusWindow = new UpdateStatusWindow { DataContext = vm };
        ShowUtilityWindow(statusWindow);

        var svc = new VelopackUpdateService();
        var checkResult = await svc.CheckForUpdateAsync(CancellationToken.None);
        var feedResult = new ReleaseFeedResult(
            checkResult.UpdateAvailable,
            checkResult.LatestVersion,
            checkResult.ChangelogUrl ?? "https://spectralis.deltavdevs.com",
            null);
        vm.ApplyUpdateFeedResult(feedResult);

        if (!feedResult.IsUpdateAvailable) return;
        if (!string.IsNullOrWhiteSpace(vm.IgnoredUpdateVersion) &&
            string.Equals(vm.IgnoredUpdateVersion, feedResult.LatestVersion, StringComparison.OrdinalIgnoreCase)) return;
        if (owner is null) return;

        var choice = await UpdatePromptWindow.ShowAsync(owner, feedResult.LatestVersion);
        switch (choice)
        {
            case UpdatePromptChoice.UpdateNow:
                statusWindow.Close();
                if (checkResult.SupportsInProcessUpdate)
                    await UpdateProgressWindow.RunAsync(owner, feedResult.LatestVersion ?? "");
                else
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        "https://spectralis.deltavdevs.com") { UseShellExecute = true });
                break;
            case UpdatePromptChoice.DontRemindAgain:
                if (!string.IsNullOrWhiteSpace(feedResult.LatestVersion))
                    vm.SaveIgnoredUpdateVersion(feedResult.LatestVersion);
                break;
        }
    }

    private async void OnInstallAnotherVersion(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var chosen = await VersionPickerWindow.ShowAsync(owner, vm.IsBetaChannel);
        if (chosen is null) return;

        try
        {
            await VersionInstallFlow.RunAsync(chosen.Version, DiagnosticsSnapshot.CurrentVersion,
                () => VersionPickerWindow.ConfirmDowngradeAsync(owner, chosen.Version),
                () => RecoveryUpdater.EnsureInstalled(),
                ready => UpdateProgressWindow.RunAsync(owner, chosen, ready),
                () => vm.HoldVersion(chosen.Version));
        }
        catch (Exception ex)
        {
            SpectralisLog.Warn("Could not prepare recovery updater: " + ex.Message);
            await MessageWindow.ShowAsync(owner, "Downgrade canceled",
                "The recovery updater could not be prepared. Make sure Spectralis can write to your user folder and try again.");
        }
    }

    private async void OnShowRecoveryUpdater(object? sender, RoutedEventArgs e)
    {
        try { RecoveryUpdater.ShowFolder(); }
        catch (Exception ex) when (TopLevel.GetTopLevel(this) is Window)
        {
            SpectralisLog.Warn("Could not open recovery updater: " + ex.Message);
            await MessageWindow.ShowAsync((Window)TopLevel.GetTopLevel(this)!, "Updater unavailable",
                "The recovery updater could not be opened. Reinstall the latest Spectralis release and try again.");
        }
    }

    private async void OnCleanupTemp(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button) button.IsEnabled = false;
        try { CleanupResult.Text = (await TempStorageCleanup.RunAsync()).ToString(); }
        catch (Exception ex) { CleanupResult.Text = "Cleanup skipped: " + ex.Message; }
        finally { if (sender is Button finished) finished.IsEnabled = true; }
    }

    private void OnOpenAbout(object? sender, RoutedEventArgs e) =>
        ShowUtilityWindow(new AboutWindow());

    private void OnOpenTerms(object? sender, RoutedEventArgs e) =>
        ShowUtilityWindow(new LegalDocumentWindow(LegalDocumentKind.TermsOfService));

    private void OnOpenPrivacy(object? sender, RoutedEventArgs e) =>
        ShowUtilityWindow(new LegalDocumentWindow(LegalDocumentKind.PrivacyPolicy));

    private void ShowUtilityWindow(Window window)
    {
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }
}
