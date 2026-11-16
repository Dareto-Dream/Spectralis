using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Spectralis.App.Services;
using Spectralis.Core.Platform;

namespace Spectralis.App.Views;

public partial class UpdateProgressWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private bool _applyPending;

    public UpdateProgressWindow()
    {
        InitializeComponent();
    }

    public static async Task RunAsync(Window owner, string version)
    {
        var win = new UpdateProgressWindow();
        win.Show(owner);
        _ = win.DownloadAsync(version);
        await win.WaitForCloseAsync();
    }

    /// <summary>Installs one specific release (the version picker), newer or older than this one.</summary>
    public static async Task RunAsync(Window owner, ReleaseInfo release)
    {
        var win = new UpdateProgressWindow();
        win.Heading.Text = $"Installing Spectralis {release.Label}…";
        win.Show(owner);
        _ = win.DownloadAsync(release.Version, release);
        await win.WaitForCloseAsync();
    }

    private async Task DownloadAsync(string version, ReleaseInfo? release = null)
    {
        var svc = new VelopackUpdateService();
        var progress = new Progress<double>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = p;
                StatusLabel.Text = $"Downloading… {p:P0}";
            });
        });

        try
        {
            if (release is null)
                await svc.DownloadAndApplyAsync(progress, _cts.Token);
            else
                await svc.InstallVersionAsync(release, progress, _cts.Token);
            // Both end in ApplyUpdatesAndRestart — app will exit.
            _applyPending = true;
        }
        catch (OperationCanceledException)
        {
            Close();
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusLabel.Text = $"Download failed: {ex.Message}";
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = 0;
            });
        }
    }

    private Task WaitForCloseAsync()
    {
        var tcs = new TaskCompletionSource();
        Closed += (_, _) => tcs.TrySetResult();
        return tcs.Task;
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        if (!_applyPending)
            _cts.Cancel();
        Close();
    }
}
