using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Spectralis.Core.Satellite;

/// <summary>A receiver's negotiated state, once past pairing and capability handshake.</summary>
public sealed class SatelliteReceiverSession
{
    public required string ConnectionId { get; init; }
    public required string DeviceId { get; init; }
    public required string DisplayName { get; init; }
    public SatelliteCodec Codec { get; init; } = SatelliteCodec.Pcm;
    public SatelliteDisplay Display { get; init; } = SatelliteDisplay.None;
}

/// <summary>Raised when a not-yet-paired receiver connects — the host app shows this PIN so the
/// person at the receiver can enter it (mirrors the existing Discord PIN pairing UX).</summary>
public sealed class SatellitePairingCodeReadyEventArgs : EventArgs
{
    public required string ConnectionId { get; init; }
    public required string DisplayName { get; init; }
    public required string Pin { get; init; }
}

/// <summary>
/// LAN-direct source-side server: accepts one TCP connection per receiver bound to every local
/// interface (not loopback-only, unlike ObsOverlayServer — this genuinely needs to be LAN
/// reachable), handles PIN pairing (skipped for already-paired devices), the capability
/// handshake, an NTP-style clock-sync responder, and broadcasting audio+FFT frames to every
/// connected, paired receiver — each per its own declared display capability (skipping FFT
/// bins for a receive-only speaker).
///
/// Concurrency note: each connection's stream is written from two places — the clock-sync
/// responder (reacting to incoming pings) and <see cref="BroadcastAudioFrameAsync"/> (reacting
/// to the audio tap). Both go through <see cref="ConnectedClient.WriteControlAsync"/> /
/// <see cref="ConnectedClient.WriteAudioAsync"/>, which serialize access with a semaphore —
/// writing to the same NetworkStream from two threads at once without that would corrupt the
/// frame stream.
/// </summary>
public sealed class SatelliteSourceServer : IAsyncDisposable
{
    private static readonly TimeSpan PairingTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan HandshakeStepTimeout = TimeSpan.FromSeconds(10);

    private readonly TcpListener _listener;
    private readonly SatellitePairedDevicesStore _pairedDevices;
    private readonly ConcurrentDictionary<string, ConnectedClient> _clients = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private SatelliteDiscovery? _discovery;

    public event EventHandler<SatellitePairingCodeReadyEventArgs>? PairingCodeReady;
    public event EventHandler<SatelliteReceiverSession>? ReceiverConnected;
    public event EventHandler<string>? ReceiverDisconnected;

    public SatelliteSourceServer(SatellitePairedDevicesStore? pairedDevices = null, int port = 0)
    {
        _pairedDevices = pairedDevices ?? new SatellitePairedDevicesStore();
        _listener = new TcpListener(IPAddress.Any, port);
    }

    /// <summary>Bound port — 0 until <see cref="Start"/> has run (0 requests an OS-assigned
    /// ephemeral port, read back here once bound).</summary>
    public int Port { get; private set; }

    public IReadOnlyCollection<SatelliteReceiverSession> ConnectedReceivers =>
        _clients.Values.Select(c => c.Session).Where(s => s is not null).Select(s => s!).ToList();

    public void Start()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    /// <summary>
    /// Opt-in: advertises this source on the LAN via mDNS/DNS-SD (<see cref="SatelliteDiscovery.ServiceType"/>)
    /// so receivers can find it without the host being typed in manually. Separate from
    /// <see cref="Start"/> deliberately — real mDNS involves an actual OS multicast socket,
    /// which every test in this codebase that spins up a SatelliteSourceServer would otherwise
    /// pay for even though none of them need LAN discovery to exercise the protocol itself.
    /// Requires <see cref="Start"/> to have run first (needs the bound port).
    /// </summary>
    public void StartAdvertising(string? instanceName = null)
    {
        if (Port == 0)
        {
            throw new InvalidOperationException($"{nameof(Start)}() must be called before {nameof(StartAdvertising)}()");
        }

        _discovery ??= new SatelliteDiscovery();
        _discovery.StartAdvertising(instanceName ?? Environment.MachineName, Port);
    }

    public void StopAdvertising() => _discovery?.StopAdvertising();

    public async ValueTask DisposeAsync()
    {
        _discovery?.Dispose();
        _cts?.Cancel();
        try
        {
            _listener.Stop();
        }
        catch
        {
            // already stopped/never started — fine
        }

        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        _clients.Clear();

        if (_acceptLoopTask is not null)
        {
            try
            {
                await _acceptLoopTask.ConfigureAwait(false);
            }
            catch
            {
                // expected on cancellation
            }
        }

        _cts?.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcpClient;
            try
            {
                tcpClient = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(tcpClient, ct), ct);
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken ct)
    {
        var connectionId = Guid.NewGuid().ToString("N");
        var stream = tcpClient.GetStream();
        ConnectedClient? connected = null;

        try
        {
            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stepCts.CancelAfter(HandshakeStepTimeout);

            var hello = await ReadControlAsync<SatelliteHelloMessage>(stream, stepCts.Token).ConfigureAwait(false);
            if (hello is null || string.IsNullOrWhiteSpace(hello.DeviceId))
            {
                return;
            }

            connected = new ConnectedClient(tcpClient, stream);

            var alreadyPaired = _pairedDevices.IsPaired(hello.DeviceId);
            if (!alreadyPaired)
            {
                var pin = SatellitePairingCode.Generate();
                PairingCodeReady?.Invoke(this, new SatellitePairingCodeReadyEventArgs
                {
                    ConnectionId = connectionId,
                    DisplayName = hello.DisplayName,
                    Pin = pin,
                });
                await connected.WriteControlAsync(new SatellitePairingRequiredMessage(), ct).ConfigureAwait(false);

                using var pairingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                pairingCts.CancelAfter(PairingTimeout);
                var pairRequest = await ReadControlAsync<SatellitePairRequestMessage>(stream, pairingCts.Token).ConfigureAwait(false);
                var ok = pairRequest is not null && SatellitePairingCode.Matches(pairRequest.Pin, pin);

                await connected.WriteControlAsync(new SatellitePairResultMessage { Ok = ok, Reason = ok ? null : "incorrect PIN" }, ct).ConfigureAwait(false);
                if (!ok)
                {
                    return;
                }

                _pairedDevices.MarkPaired(hello.DeviceId, hello.DisplayName);
            }
            else
            {
                await connected.WriteControlAsync(new SatellitePairResultMessage { Ok = true }, ct).ConfigureAwait(false);
            }

            var caps = await ReadControlAsync<SatelliteCapabilitiesMessage>(stream, stepCts.Token).ConfigureAwait(false);

            var session = new SatelliteReceiverSession
            {
                ConnectionId = connectionId,
                DeviceId = hello.DeviceId,
                DisplayName = hello.DisplayName,
                Codec = caps?.Codec ?? SatelliteCodec.Pcm,
                Display = caps?.Display ?? SatelliteDisplay.None,
            };
            connected.Session = session;
            _clients[connectionId] = connected;
            ReceiverConnected?.Invoke(this, session);

            await RunControlLoopAsync(connected, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // handshake step timed out, or server shutting down — treat as a dropped connection
        }
        catch (IOException)
        {
            // connection reset mid-handshake/loop — treat as a dropped connection
        }
        finally
        {
            _clients.TryRemove(connectionId, out _);
            connected?.Dispose();
            ReceiverDisconnected?.Invoke(this, connectionId);
        }
    }

    /// <summary>After the handshake, the only thing a receiver sends unsolicited is
    /// clock-sync pings — everything else (audio) flows source -> receiver only.</summary>
    private async Task RunControlLoopAsync(ConnectedClient client, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var frame = await SatelliteFraming.ReadAsync(client.Stream, ct).ConfigureAwait(false);
            if (frame is null)
            {
                return; // clean disconnect
            }

            if (frame.Value.Type != SatelliteFrameType.Control)
            {
                continue;
            }

            if (SatelliteMessageReader.PeekType(frame.Value.Payload) != SatelliteProtocol.ClockPing)
            {
                continue;
            }

            var ping = SatelliteMessageReader.Deserialize<SatelliteClockPingMessage>(frame.Value.Payload);
            if (ping is null)
            {
                continue;
            }

            var t1 = SatelliteClock.NowMs();
            var pong = new SatelliteClockPongMessage { T0 = ping.T0, T1 = t1, T2 = SatelliteClock.NowMs() };
            await client.WriteControlAsync(pong, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Sends one audio+FFT frame to every connected, paired receiver — per-receiver,
    /// strips the FFT bins for anyone who declared <see cref="SatelliteDisplay.None"/>, and
    /// re-encodes to Opus for anyone who negotiated it (each receiver has its own encoder
    /// pipeline, so a raw PCM block may turn into zero, one, or more Opus packets per receiver
    /// depending on that receiver's own buffering state). Errors writing to one receiver don't
    /// affect delivery to the others.</summary>
    public async Task BroadcastAudioFrameAsync(SatelliteAudioFrame frame, CancellationToken ct = default)
    {
        foreach (var client in _clients.Values)
        {
            if (client.Session is not { } session)
            {
                continue; // still mid-handshake
            }

            float[] fftBins = session.Display == SatelliteDisplay.None ? [] : frame.FftBins;

            try
            {
                await client.WriteAudioAsync(frame, fftBins, ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // that receiver's connection is on its way out — the accept/handle loop's own
                // exception handling will clean it up; don't let it interrupt the broadcast.
            }
        }
    }

    private static async Task<T?> ReadControlAsync<T>(NetworkStream stream, CancellationToken ct) where T : class
    {
        var frame = await SatelliteFraming.ReadAsync(stream, ct).ConfigureAwait(false);
        if (frame is not { Type: SatelliteFrameType.Control })
        {
            return null;
        }

        return SatelliteMessageReader.Deserialize<T>(frame.Value.Payload);
    }

    private sealed class ConnectedClient(TcpClient tcpClient, NetworkStream stream) : IDisposable
    {
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private SatelliteOpusEncodePipeline? _opusPipeline;
        private bool _opusPipelineAttempted;

        public NetworkStream Stream { get; } = stream;
        public SatelliteReceiverSession? Session { get; set; }

        public async Task WriteControlAsync<T>(T message, CancellationToken ct) where T : class
        {
            var payload = SatelliteMessageReader.Serialize(message);
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await SatelliteFraming.WriteAsync(Stream, SatelliteFrameType.Control, payload, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>Encodes <paramref name="frame"/>'s PCM for this specific receiver — straight
        /// through for Pcm, or via this connection's own Opus pipeline (lazily created, reused
        /// for the life of the connection) when the receiver negotiated Opus and the source's
        /// sample rate is Opus-native. A raw block can turn into zero or several Opus packets
        /// here depending on this connection's buffering state, so this may write more than one
        /// wire frame per call. <paramref name="fftBins"/> is attached to only the first (or
        /// only) frame emitted — it's a point-in-time visualizer snapshot, not something that
        /// needs to repeat across every Opus packet a single raw block happened to produce.</summary>
        public async Task WriteAudioAsync(SatelliteAudioFrame frame, float[] fftBins, CancellationToken ct)
        {
            var wantsOpus = Session?.Codec == SatelliteCodec.Opus;
            var pipeline = wantsOpus ? GetOrCreateOpusPipeline(frame.SampleRate, frame.ChannelCount) : null;

            if (pipeline is null)
            {
                var outgoing = ReferenceEquals(fftBins, frame.FftBins)
                    ? frame
                    : new SatelliteAudioFrame
                    {
                        SourceClockMs = frame.SourceClockMs,
                        SampleRate = frame.SampleRate,
                        ChannelCount = frame.ChannelCount,
                        Encoding = frame.Encoding,
                        PcmSamples = frame.PcmSamples,
                        FftBins = fftBins,
                    };
                await WriteOneAsync(outgoing, ct).ConfigureAwait(false);
                return;
            }

            var packets = pipeline.Encode(frame.PcmSamples);
            for (var i = 0; i < packets.Count; i++)
            {
                var outgoing = new SatelliteAudioFrame
                {
                    SourceClockMs = frame.SourceClockMs,
                    SampleRate = frame.SampleRate,
                    ChannelCount = frame.ChannelCount,
                    Encoding = SatelliteAudioEncoding.Opus,
                    OpusPayload = packets[i],
                    OpusFrameSize = pipeline.FrameSizePerChannel,
                    FftBins = i == 0 ? fftBins : [],
                };
                await WriteOneAsync(outgoing, ct).ConfigureAwait(false);
            }
        }

        private SatelliteOpusEncodePipeline? GetOrCreateOpusPipeline(int sampleRate, int channels)
        {
            if (_opusPipelineAttempted)
            {
                return _opusPipeline;
            }

            _opusPipelineAttempted = true;
            if (SatelliteOpusCodec.IsRateSupported(sampleRate))
            {
                _opusPipeline = new SatelliteOpusEncodePipeline(sampleRate, channels);
            }

            return _opusPipeline;
        }

        private async Task WriteOneAsync(SatelliteAudioFrame frame, CancellationToken ct)
        {
            var payload = frame.Encode();
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await SatelliteFraming.WriteAsync(Stream, SatelliteFrameType.Audio, payload, ct).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Dispose()
        {
            _writeLock.Dispose();
            tcpClient.Dispose();
        }
    }
}
