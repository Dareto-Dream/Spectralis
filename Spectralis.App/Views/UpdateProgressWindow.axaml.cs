using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Spectralis.App.Services;

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

    private async Task DownloadAsync(string version)
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
            await svc.DownloadAndApplyAsync(progress, _cts.Token);
            // DownloadAndApplyAsync calls ApplyUpdatesAndRestart — app will exit.
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
