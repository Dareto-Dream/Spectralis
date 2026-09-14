using Spectralis.Core.Satellite;
using Xunit;

namespace Spectralis.Tests.Integration;

/// <summary>
/// End-to-end proof of the Satellite protocol over real loopback TCP sockets — a genuine
/// SatelliteSourceServer talking to a genuine SatelliteReceiverClient, not mocks. Covers the
/// full lifecycle: first-time PIN pairing, wrong-PIN rejection, reconnecting without
/// re-pairing, capability-driven FFT stripping, clock sync, and multi-receiver broadcast.
/// </summary>
public sealed class SatelliteIntegrationTests : IAsyncLifetime
{
    private readonly string _pairedDevicesPath = Path.Combine(Path.GetTempPath(), $"satellite-paired-{Guid.NewGuid():N}.json");
    private SatelliteSourceServer _server = null!;
    private SatellitePairedDevicesStore _pairedDevices = null!;

    public Task InitializeAsync()
    {
        _pairedDevices = new SatellitePairedDevicesStore(_pairedDevicesPath);
        _server = new SatelliteSourceServer(_pairedDevices, port: 0);
        _server.Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        if (File.Exists(_pairedDevicesPath))
        {
            File.Delete(_pairedDevicesPath);
        }
    }

    private async Task<(SatelliteReceiverClient Client, bool Connected)> ConnectAsync(
        string deviceId, string displayName = "Test Receiver",
        SatelliteCodec codec = SatelliteCodec.Pcm, SatelliteDisplay display = SatelliteDisplay.Fft,
        Func<Task<string>>? obtainPin = null)
    {
        var client = new SatelliteReceiverClient();
        var connected = await client.ConnectAsync("127.0.0.1", _server.Port, deviceId, displayName, codec, display, obtainPin)
            .WaitAsync(TimeSpan.FromSeconds(10));
        return (client, connected);
    }

    [Fact]
    public async Task FirstTimeConnection_WithCorrectPin_Succeeds()
    {
        string? shownPin = null;
        _server.PairingCodeReady += (_, e) => shownPin = e.Pin;

        var (client, connected) = await ConnectAsync("device-1", obtainPin: () => Task.FromResult(shownPin!));
        await using var _ = client;

        Assert.True(connected);
        Assert.True(client.IsConnected);
        Assert.NotNull(shownPin);
        Assert.Equal(6, shownPin!.Length);
    }

    [Fact]
    public async Task FirstTimeConnection_WithWrongPin_Fails()
    {
        var (client, connected) = await ConnectAsync("device-2", obtainPin: () => Task.FromResult("000000"));
        await using var _ = client;

        Assert.False(connected);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task FirstTimeConnection_NoObtainPinCallback_FailsCleanly()
    {
        // Simulates a receiver that doesn't support interactive pairing at all.
        var (client, connected) = await ConnectAsync("device-3", obtainPin: null);
        await using var _ = client;

        Assert.False(connected);
    }

    [Fact]
    public async Task AlreadyPairedDevice_SkipsPinEntirely()
    {
        _pairedDevices.MarkPaired("device-4", "Known Receiver");
        var pairingRaised = false;
        _server.PairingCodeReady += (_, _) => pairingRaised = true;

        var (client, connected) = await ConnectAsync("device-4", obtainPin: null); // no PIN needed
        await using var _ = client;

        Assert.True(connected);
        Assert.False(pairingRaised, "pairing should not be requested for an already-known device");
    }

    [Fact]
    public async Task ReceiverConnected_EventFiresWithNegotiatedCapabilities()
    {
        _pairedDevices.MarkPaired("device-5", "Known Receiver");
        SatelliteReceiverSession? session = null;
        _server.ReceiverConnected += (_, s) => session = s;

        var (client, connected) = await ConnectAsync("device-5", codec: SatelliteCodec.Opus, display: SatelliteDisplay.Screen, obtainPin: null);
        await using var _ = client;
        Assert.True(connected);

        await WaitUntilAsync(() => session is not null);
        Assert.Equal("device-5", session!.DeviceId);
        Assert.Equal(SatelliteCodec.Opus, session.Codec);
        Assert.Equal(SatelliteDisplay.Screen, session.Display);
    }

    [Fact]
    public async Task BroadcastAudioFrame_ArrivesAtReceiverIntact()
    {
        _pairedDevices.MarkPaired("device-6", "Known Receiver");
        var (client, connected) = await ConnectAsync("device-6", display: SatelliteDisplay.Fft, obtainPin: null);
        await using var _ = client;
        Assert.True(connected);
        await WaitUntilAsync(() => _server.ConnectedReceivers.Count == 1);

        SatelliteAudioFrame? received = null;
        client.AudioFrameReceived += (_, frame) => received = frame;

        var sent = new SatelliteAudioFrame
        {
            SourceClockMs = 42.5,
            SampleRate = 48000,
            ChannelCount = 2,
            PcmSamples = [0.1f, -0.2f, 0.3f, -0.4f],
            FftBins = [0.5f, 0.6f, 0.7f],
        };
        await _server.BroadcastAudioFrameAsync(sent);

        await WaitUntilAsync(() => received is not null);
        Assert.Equal(sent.PcmSamples, received!.PcmSamples);
        Assert.Equal(sent.FftBins, received.FftBins);
        Assert.Equal(sent.SampleRate, received.SampleRate);
    }

    [Fact]
    public async Task BroadcastAudioFrame_StripsFftForDisplayNoneReceiver()
    {
        _pairedDevices.MarkPaired("device-7", "Speaker Only");
        var (client, connected) = await ConnectAsync("device-7", display: SatelliteDisplay.None, obtainPin: null);
        await using var _ = client;
        Assert.True(connected);
        await WaitUntilAsync(() => _server.ConnectedReceivers.Count == 1);

        SatelliteAudioFrame? received = null;
        client.AudioFrameReceived += (_, frame) => received = frame;

        await _server.BroadcastAudioFrameAsync(new SatelliteAudioFrame
        {
            SourceClockMs = 1,
            SampleRate = 48000,
            ChannelCount = 1,
            PcmSamples = [0.1f],
            FftBins = [1f, 2f, 3f], // source computed FFT anyway — receiver shouldn't get it
        });

        await WaitUntilAsync(() => received is not null);
        Assert.Empty(received!.FftBins);
        Assert.Single(received.PcmSamples); // audio itself is unaffected
    }

    [Fact]
    public async Task ClockSync_ProducesASensibleLoopbackOffsetAndDelay()
    {
        _pairedDevices.MarkPaired("device-8", "Known Receiver");
        var (client, connected) = await ConnectAsync("device-8", obtainPin: null);
        await using var _ = client;
        Assert.True(connected);

        await client.PingClockAsync();
        await WaitUntilAsync(() => client.ClockSync.OffsetMs is not null);

        // Both endpoints are the same process on loopback — round-trip delay should be small
        // (well under the ~10ms target the roadmap calls for over a real LAN), and the offset
        // should be close to zero since it's the same clock on both "sides" of this test.
        Assert.True(client.ClockSync.RoundTripDelayMs < 50, $"unexpectedly high loopback delay: {client.ClockSync.RoundTripDelayMs}ms");
        Assert.True(Math.Abs(client.ClockSync.OffsetMs!.Value) < 50, $"unexpectedly large offset for same-clock loopback: {client.ClockSync.OffsetMs}ms");
    }

    [Fact]
    public async Task MultipleReceivers_EachGetTheirOwnCopyOfABroadcastFrame()
    {
        _pairedDevices.MarkPaired("device-9a", "Receiver A");
        _pairedDevices.MarkPaired("device-9b", "Receiver B");

        var (clientA, connectedA) = await ConnectAsync("device-9a", display: SatelliteDisplay.Fft, obtainPin: null);
        var (clientB, connectedB) = await ConnectAsync("device-9b", display: SatelliteDisplay.None, obtainPin: null);
        await using var _a = clientA;
        await using var _b = clientB;
        Assert.True(connectedA);
        Assert.True(connectedB);
        await WaitUntilAsync(() => _server.ConnectedReceivers.Count == 2);

        SatelliteAudioFrame? receivedA = null;
        SatelliteAudioFrame? receivedB = null;
        clientA.AudioFrameReceived += (_, f) => receivedA = f;
        clientB.AudioFrameReceived += (_, f) => receivedB = f;

        await _server.BroadcastAudioFrameAsync(new SatelliteAudioFrame
        {
            SourceClockMs = 0,
            SampleRate = 44100,
            ChannelCount = 2,
            PcmSamples = [1f, 2f],
            FftBins = [9f, 8f, 7f],
        });

        await WaitUntilAsync(() => receivedA is not null && receivedB is not null);
        Assert.Equal(3, receivedA!.FftBins.Length); // declared Fft — keeps the bins
        Assert.Empty(receivedB!.FftBins);            // declared None — bins stripped
    }

    [Fact]
    public async Task ReceiverDisconnected_FiresWhenClientDisposes()
    {
        _pairedDevices.MarkPaired("device-10", "Known Receiver");
        var disconnectedConnectionId = new TaskCompletionSource<string>();
        _server.ReceiverDisconnected += (_, connId) => disconnectedConnectionId.TrySetResult(connId);

        var (client, connected) = await ConnectAsync("device-10", obtainPin: null);
        Assert.True(connected);
        await WaitUntilAsync(() => _server.ConnectedReceivers.Count == 1);

        await client.DisposeAsync();

        var result = await disconnectedConnectionId.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(string.IsNullOrEmpty(result));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("condition not met within timeout");
            }

            await Task.Delay(10);
        }
    }
}
