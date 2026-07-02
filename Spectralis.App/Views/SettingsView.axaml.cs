using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || vm.StreamerSettings is not { } ss) return;
        DeadZoneDesigner.SetAspectRatio(ss.CanvasAspectRatio);
        DeadZoneDesigner.LoadZones(ss.GetDeadZones());
        DeadZoneDesigner.ZonesChanged -= OnDeadZonesChanged;
        DeadZoneDesigner.ZonesChanged += OnDeadZonesChanged;
    }

    private void OnDeadZonesChanged(object? sender, EventArgs e)
    {
        if (DataContext is SettingsViewModel { StreamerSettings: { } ss })
            ss.SaveDeadZones(DeadZoneDesigner.CollectZones());
    }

    private void OnApplyDeadZones(object? sender, RoutedEventArgs e) =>
        (DataContext as SettingsViewModel)?.StreamerSettings?.ApplyToCurrentLayout();

    private void OnRemoveSelectedDeadZone(object? sender, RoutedEventArgs e)
    {
        DeadZoneDesigner.RemoveSelected();
    }

    private void OnClearDeadZones(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel { StreamerSettings: { } ss }) return;
        ss.ClearAll();
        DeadZoneDesigner.LoadZones([]);
    }

    private void OnToggleDeadZonePreview(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel { StreamerSettings: { } ss }) return;
        var enabled = sender is ToggleButton { IsChecked: true };
        DeadZoneDesigner.SetPreviewMode(enabled, enabled ? ss.GetPreviewWidgets() : null);
    }

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

    private void OnVersionTextPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) =>
        (DataContext as SettingsViewModel)?.RegisterVersionClick();

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
