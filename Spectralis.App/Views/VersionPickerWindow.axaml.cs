using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Spectralis.App.Services;
using Spectralis.Core.Platform;

namespace Spectralis.App.Views;

/// <summary>
/// Lists the published releases for this platform (from the release index) and lets the user pick
/// one to install, older ones included. It only chooses; the caller runs the install.
/// </summary>
public partial class VersionPickerWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly bool _includeBetas;
    private ReleaseInfo? _chosen;

    /// <summary>One list row. The ListBox shows <see cref="ToString"/>.</summary>
    private sealed record Row(ReleaseInfo Release, string Text)
    {
        public override string ToString() => Text;
    }

    public VersionPickerWindow() : this(false)
    {
    }

    public VersionPickerWindow(bool includeBetas)
    {
        _includeBetas = includeBetas;
        InitializeComponent();
        Opened += async (_, _) => await LoadAsync();
        Closed += (_, _) => _cts.Cancel();
    }

    /// <summary>Returns the release the user chose to install, or null if they backed out.</summary>
    public static async Task<ReleaseInfo?> ShowAsync(Window owner, bool includeBetas)
    {
        var window = new VersionPickerWindow(includeBetas);
        await window.ShowDialog(owner);
        return window._chosen;
    }

    private async Task LoadAsync()
    {
        try
        {
            var current = DiagnosticsSnapshot.CurrentVersion;
            var releases = await ReleaseIndex.FetchAsync(VelopackUpdateService.Channel, _cts.Token);
            // Betas are only offered to people who opted into them, or who are already on one.
            var showBetas = _includeBetas || ReleaseVersion.IsPrerelease(current);
            var rows = releases
                .Where(r => showBetas || !r.IsBeta)
                .Select(r => new Row(r, Describe(r, current)))
                .ToList();

            VersionList.ItemsSource = rows;
            StatusLabel.Text = rows.Count == 0
                ? "No other versions are available for this platform yet."
                : $"You are on {current}.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Couldn't load the list of releases: {ex.Message}";
        }
    }

    private static string Describe(ReleaseInfo release, string current)
    {
        var text = release.Label;
        if (!string.IsNullOrWhiteSpace(release.Date))
        {
            text += $"   ·   {release.Date}";
        }

        if (ReleaseVersion.Compare(release.Version, current) == 0)
        {
            text += "   ·   installed";
        }

        return text;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var release = (VersionList.SelectedItem as Row)?.Release;
        var installed = release is not null &&
                        ReleaseVersion.Compare(release.Version, DiagnosticsSnapshot.CurrentVersion) == 0;
        InstallButton.IsEnabled = release is not null && !installed;
        NotesButton.IsEnabled = !string.IsNullOrEmpty(release?.NotesUrl);
        if (release is not null)
        {
            var older = ReleaseVersion.Compare(release.Version, DiagnosticsSnapshot.CurrentVersion) < 0;
            StatusLabel.Text = installed
                ? "This is the version you are running."
                : older
                    ? $"{release.Label} is older than what you have. Anything saved by newer features may not be understood by it."
                    : $"{release.Label} is newer than what you have.";
        }
    }

    private void OnOpenNotes(object? sender, RoutedEventArgs e)
    {
        var url = (VersionList.SelectedItem as Row)?.Release.NotesUrl;
        if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.Ordinal))
        {
            return;
        }

        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser: nothing useful to do */ }
    }

    private void OnInstall(object? sender, RoutedEventArgs e)
    {
        _chosen = (VersionList.SelectedItem as Row)?.Release;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
