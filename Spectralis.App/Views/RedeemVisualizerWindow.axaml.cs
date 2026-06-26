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
            SetStatus("Enter a redeem key first.", isError: true);
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        RedeemButton.IsEnabled = false;
        SetStatus("Contacting DeltaVDevs CDN…", isError: false);

        try
        {
            var package = await _client.RedeemAsync(key, _cts.Token);
            _store.Install(package);
            SetStatus($"✓ {package.DisplayName} installed successfully.", isError: false);
            RedeemKeyBox.Text = string.Empty;
            RefreshInstalled();
        }
        catch (OperationCanceledException)
        {
            SetStatus(string.Empty, isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            RedeemButton.IsEnabled = true;
        }
    }

    private void SetStatus(string text, bool isError)
    {
        StatusLabel.Text = text;
        StatusLabel.IsVisible = !string.IsNullOrEmpty(text);
        StatusLabel.Classes.Set("signal", isError);
    }

    private void OnRedeemKeyBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OnRedeem(sender, new RoutedEventArgs());
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }
}
