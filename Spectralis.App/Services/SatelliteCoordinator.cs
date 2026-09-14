using Spectralis.Core.Audio;
using Spectralis.Core.Satellite;

namespace Spectralis.App.Services;

/// <summary>
/// App-level owner of the Satellite source side: a <see cref="SatelliteSourceServer"/> (pairing
/// + protocol), a <see cref="SatelliteAudioBroadcaster"/> (taps the real playback engine), and
/// mDNS advertising — started/stopped together as one unit from the UI. Mirrors how
/// <c>ObsOverlayCoordinator</c>/<c>DiscordPresenceCoordinator</c> own their respective
/// always-there-but-optional services.
/// </summary>
public sealed class SatelliteCoordinator : IAsyncDisposable
{
    private readonly AudioEngine _engine;
    private readonly SatellitePairedDevicesStore _pairedDevices;
    private SatelliteSourceServer? _server;
    private SatelliteAudioBroadcaster? _broadcaster;

    public event EventHandler<SatellitePairingCodeReadyEventArgs>? PairingCodeReady;
    public event EventHandler<SatelliteReceiverSession>? ReceiverConnected;
    public event EventHandler<string>? ReceiverDisconnected;

    public SatelliteCoordinator(AudioEngine engine, SatellitePairedDevicesStore? pairedDevices = null)
    {
        _engine = engine;
        _pairedDevices = pairedDevices ?? new SatellitePairedDevicesStore();
    }

    public bool IsRunning => _server is not null;

    public int Port => _server?.Port ?? 0;

    public IReadOnlyCollection<SatelliteReceiverSession> ConnectedReceivers => _server?.ConnectedReceivers ?? [];

    public IReadOnlyList<SatellitePairedDevice> PairedDevices => _pairedDevices.AllDevices;

    public void ForgetPairedDevice(string deviceId) => _pairedDevices.Forget(deviceId);

    /// <summary>Starts the source server, mDNS advertising, and the audio tap together. A
    /// second call while already running is a no-op — call <see cref="StopAsync"/> first to
    /// restart (e.g. to pick up a new instance name).</summary>
    public void Start(string? instanceName = null)
    {
        if (_server is not null)
        {
            return;
        }

        var server = new SatelliteSourceServer(_pairedDevices, port: 0);
        server.PairingCodeReady += OnPairingCodeReady;
        server.ReceiverConnected += OnReceiverConnected;
        server.ReceiverDisconnected += OnReceiverDisconnected;
        server.Start();
        server.StartAdvertising(instanceName ?? Environment.MachineName);
        _server = server;

        var broadcaster = new SatelliteAudioBroadcaster(_engine);
        broadcaster.Attach(server);
        _broadcaster = broadcaster;
    }

    public async Task StopAsync()
    {
        _broadcaster?.Dispose();
        _broadcaster = null;

        if (_server is { } server)
        {
            server.PairingCodeReady -= OnPairingCodeReady;
            server.ReceiverConnected -= OnReceiverConnected;
            server.ReceiverDisconnected -= OnReceiverDisconnected;
            await server.DisposeAsync().ConfigureAwait(false);
            _server = null;
        }
    }

    private void OnPairingCodeReady(object? sender, SatellitePairingCodeReadyEventArgs e) => PairingCodeReady?.Invoke(this, e);

    private void OnReceiverConnected(object? sender, SatelliteReceiverSession e) => ReceiverConnected?.Invoke(this, e);

    private void OnReceiverDisconnected(object? sender, string e) => ReceiverDisconnected?.Invoke(this, e);

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
