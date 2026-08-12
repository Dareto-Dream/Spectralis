using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Spectralis.Core.Visualizers.Installed;

namespace Spectralis.App.Views;

public partial class RedeemVisualizerWindow : Window
{
    private readonly InstalledVisualizerStore _store = new();
    private readonly RedeemableVisualizerClient _client = new();
    private CancellationTokenSource? _cts;

    /// <summary>Set by the caller to open the Scripted Visualizers editor from this window.</summary>
    public Action? OpenScriptedVisualizersRequested { get; set; }

    public RedeemVisualizerWindow()
    {
        InitializeComponent();
        Closed += (_, _) => _client.Dispose();
        RefreshInstalled();
    }

    private void RefreshInstalled()
    {
        var all = _store.LoadAll();
        InstalledPanel.IsVisible = all.Count > 0;
        InstalledCountLabel.Text = $"{all.Count} visualizer{(all.Count == 1 ? "" : "s")} installed";
        InstalledList.ItemsSource = all.Select(static d =>
        {
            var tb = new TextBlock
            {
                Text = d.Version is not null ? $"{d.DisplayName}  v{d.Version}" : d.DisplayName,
            };
            tb.Classes.Add("secondary");
            return (object)tb;
        }).ToArray();
    }

    private async void OnRedeem(object? sender, RoutedEventArgs e)
    {
        var key = RedeemKeyBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            SetStatus("Enter a redeem key first.", StatusKind.Error);
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        RedeemButton.IsEnabled = false;
        SetStatus("Contacting DeltaVDevs CDN…", StatusKind.Neutral);

        try
        {
            var package = await _client.RedeemAsync(key, _cts.Token);
            _store.Install(package);
            SetStatus($"✓ {package.DisplayName} installed successfully.", StatusKind.Success);
            RedeemKeyBox.Text = string.Empty;
            RefreshInstalled();
        }
        catch (OperationCanceledException)
        {
            SetStatus(string.Empty, StatusKind.Neutral);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, StatusKind.Error);
        }
        finally
        {
            RedeemButton.IsEnabled = true;
        }
    }

    private enum StatusKind { Neutral, Success, Error }

    private void SetStatus(string text, StatusKind kind)
    {
        StatusLabel.Text = text;
        StatusLabel.IsVisible = !string.IsNullOrEmpty(text);
        StatusLabel.Classes.Set("error", kind == StatusKind.Error);
        StatusLabel.Classes.Set("success", kind == StatusKind.Success);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnRedeemKeyBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OnRedeem(sender, new RoutedEventArgs());
        }
    }

    private async void OnClearAll(object? sender, RoutedEventArgs e)
    {
        var count = _store.Count();
        if (count == 0)
        {
            return;
        }

        var confirmed = await ConfirmWindow.ShowAsync(this,
            "Clear Redeemed Visualizers",
            $"Remove all {count} installed visualizer{(count == 1 ? "" : "s")} from this device?",
            "Clear", "Cancel");
        if (!confirmed)
        {
            return;
        }

        try
        {
            _store.ClearAll();
            RefreshInstalled();
            SetStatus("All redeemed visualizers have been removed.", StatusKind.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not clear redeemed visualizers: {ex.Message}", StatusKind.Error);
        }
    }

    private void OnOpenScriptedVisualizers(object? sender, RoutedEventArgs e) =>
        OpenScriptedVisualizersRequested?.Invoke();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
