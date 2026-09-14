using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Spectralis.App.Services;
using Spectralis.Core.Satellite;

namespace Spectralis.App.Views;

/// <summary>Shows Satellite's status, the pairing PIN when a new device connects, currently
/// connected receivers, and previously-paired devices. The <see cref="SatelliteCoordinator"/>
/// itself is long-lived (owned by MainWindowViewModel, like ObsOverlay/DiscordPresence) —
/// closing this window is just closing the view onto it, not stopping an active broadcast.</summary>
public partial class SatelliteSourceWindow : Window
{
    private SatelliteCoordinator? _coordinator;

    public SatelliteSourceWindow(SatelliteCoordinator coordinator)
    {
        InitializeComponent();
        _coordinator = coordinator;
        _coordinator.PairingCodeReady += OnPairingCodeReady;
        _coordinator.ReceiverConnected += OnReceiverConnected;
        _coordinator.ReceiverDisconnected += OnReceiverDisconnected;
        Closed += OnClosed;

        RefreshStatus();
        RefreshPairedList();
    }

    private void OnToggleClicked(object? sender, RoutedEventArgs e)
    {
        if (_coordinator is null)
        {
            return;
        }

        if (_coordinator.IsRunning)
        {
            _ = StopAsync();
        }
        else
        {
            _coordinator.Start();
            RefreshStatus();
        }
    }

    private async Task StopAsync()
    {
        if (_coordinator is null)
        {
            return;
        }

        await _coordinator.StopAsync();
        PairingPanel.IsVisible = false;
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (_coordinator is null)
        {
            return;
        }

        if (_coordinator.IsRunning)
        {
            StatusLabel.Text = $"Broadcasting as {Environment.MachineName} on port {_coordinator.Port}";
            ToggleButton.Content = "Stop Broadcasting";
        }
        else
        {
            StatusLabel.Text = "Not running";
            ToggleButton.Content = "Start Broadcasting";
        }

        RefreshConnectedList();
    }

    private void OnPairingCodeReady(object? sender, SatellitePairingCodeReadyEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            PairingDeviceLabel.Text = $"\"{e.DisplayName}\" wants to pair";
            PairingPinLabel.Text = e.Pin;
            PairingPanel.IsVisible = true;
        });

    private void OnReceiverConnected(object? sender, SatelliteReceiverSession e) =>
        Dispatcher.UIThread.Post(() =>
        {
            PairingPanel.IsVisible = false;
            RefreshConnectedList();
            RefreshPairedList();
        });

    private void OnReceiverDisconnected(object? sender, string e) =>
        Dispatcher.UIThread.Post(RefreshConnectedList);

    private void RefreshConnectedList()
    {
        if (_coordinator is null)
        {
            return;
        }

        var items = _coordinator.ConnectedReceivers
            .Select(r => $"{r.DisplayName} — {r.Codec}, display: {r.Display}")
            .ToList();
        ConnectedList.ItemsSource = items.Count > 0 ? items : ["No devices connected"];
    }

    private void RefreshPairedList()
    {
        if (_coordinator is null)
        {
            return;
        }

        PairedList.ItemsSource = _coordinator.PairedDevices
            .Select(d => new PairedDeviceRow(d.DeviceId, $"{d.DisplayName} — paired {d.PairedAtUtc:yyyy-MM-dd}"))
            .ToList();
    }

    private void OnForgetDeviceClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string deviceId } && _coordinator is not null)
        {
            _coordinator.ForgetPairedDevice(deviceId);
            RefreshPairedList();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_coordinator is null)
        {
            return;
        }

        _coordinator.PairingCodeReady -= OnPairingCodeReady;
        _coordinator.ReceiverConnected -= OnReceiverConnected;
        _coordinator.ReceiverDisconnected -= OnReceiverDisconnected;
        _coordinator = null;
    }

}

/// <summary>Top-level (not nested) so Avalonia's compiled-bindings XAML compiler can resolve
/// it via x:DataType — a private nested type isn't reachable from the generated binding code.</summary>
public sealed record PairedDeviceRow(string DeviceId, string Label);
