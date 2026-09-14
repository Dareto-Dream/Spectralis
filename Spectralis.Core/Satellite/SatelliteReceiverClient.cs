using System.Net.Sockets;

namespace Spectralis.Core.Satellite;

/// <summary>
/// Reference receiver: connects to a <see cref="SatelliteSourceServer"/>, completes the
/// hello/pairing/capability handshake, runs a periodic clock-sync loop, and surfaces incoming
/// audio+FFT frames. This is the "one reference receiver" the roadmap calls for — proving the
/// protocol against real sockets, not a polished device-side app. A real ESP32-class receiver
/// would reimplement this same handshake/framing against the documented wire format.
/// </summary>
public sealed class SatelliteReceiverClient : IAsyncDisposable
{
    private static readonly TimeSpan ClockSyncInterval = TimeSpan.FromSeconds(2);

    private readonly TcpClient _tcpClient = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoopTask;
    private Task? _clockSyncLoopTask;
    private SatelliteOpusDecodePipeline? _opusDecoder;

    public SatelliteClockSyncEstimator ClockSync { get; } = new();

    public bool IsConnected { get; private set; }

    /// <summary>Fires for every received audio frame with <see cref="SatelliteAudioFrame.PcmSamples"/>
    /// always populated — for an Opus-encoded frame, this client decodes it internally (one
    /// decoder instance reused for the life of the connection, since Opus decoding carries
    /// running state across packets) before raising this event, so consumers never need to know
    /// which codec was negotiated.</summary>
    public event EventHandler<SatelliteAudioFrame>? AudioFrameReceived;
    public event EventHandler? Disconnected;

    /// <summary>
    /// Connects and completes the full handshake. <paramref name="obtainPin"/> is called only
    /// if the source says pairing is required (a first-time device) — a real receiver would
    /// prompt whoever's in front of it; a test just returns a known value. Returns false (never
    /// throws for an ordinary rejected/failed handshake) if any step fails — connection refused,
    /// wrong PIN, or the source rejects for any other reason.
    /// </summary>
    public async Task<bool> ConnectAsync(
        string host,
        int port,
        string deviceId,
        string displayName,
        SatelliteCodec codec,
        SatelliteDisplay display,
        Func<Task<string>>? obtainPin,
        CancellationToken ct = default)
    {
        try
        {
            await _tcpClient.ConnectAsync(host, port, ct).ConfigureAwait(false);
        }
        catch (SocketException)
        {
            return false;
        }

        _stream = _tcpClient.GetStream();

        await WriteControlAsync(new SatelliteHelloMessage { DeviceId = deviceId, DisplayName = displayName }, ct).ConfigureAwait(false);

        var afterHello = await SatelliteFraming.ReadAsync(_stream, ct).ConfigureAwait(false);
        if (afterHello is not { Type: SatelliteFrameType.Control })
        {
            return false;
        }

        switch (SatelliteMessageReader.PeekType(afterHello.Value.Payload))
        {
            case SatelliteProtocol.PairingRequired:
            {
                if (obtainPin is null)
                {
                    return false;
                }

                var pin = await obtainPin().ConfigureAwait(false);
                await WriteControlAsync(new SatellitePairRequestMessage { Pin = pin }, ct).ConfigureAwait(false);

                var resultFrame = await SatelliteFraming.ReadAsync(_stream, ct).ConfigureAwait(false);
                var result = resultFrame is { Type: SatelliteFrameType.Control }
                    ? SatelliteMessageReader.Deserialize<SatellitePairResultMessage>(resultFrame.Value.Payload)
                    : null;
                if (result is not { Ok: true })
                {
                    return false;
                }

                break;
            }

            case SatelliteProtocol.PairResult:
            {
                var result = SatelliteMessageReader.Deserialize<SatellitePairResultMessage>(afterHello.Value.Payload);
                if (result is not { Ok: true })
                {
                    return false;
                }

                break;
            }

            default:
                return false;
        }

        await WriteControlAsync(new SatelliteCapabilitiesMessage { Codec = codec, Display = display }, ct).ConfigureAwait(false);

        IsConnected = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _clockSyncLoopTask = Task.Run(() => ClockSyncLoopAsync(_cts.Token));
        return true;
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await SatelliteFraming.ReadAsync(_stream!, ct).ConfigureAwait(false);
                if (frame is null)
                {
                    break; // source closed the connection
                }

                if (frame.Value.Type == SatelliteFrameType.Audio)
                {
                    var audioFrame = SatelliteAudioFrame.Decode(frame.Value.Payload);
                    if (audioFrame is { Encoding: SatelliteAudioEncoding.Opus })
                    {
                        _opusDecoder ??= new SatelliteOpusDecodePipeline(audioFrame.SampleRate, audioFrame.ChannelCount);
                        var pcm = _opusDecoder.Decode(audioFrame.OpusPayload, audioFrame.OpusFrameSize);
                        audioFrame = audioFrame.WithPcmSamples(pcm);
                    }

                    if (audioFrame is not null)
                    {
                        AudioFrameReceived?.Invoke(this, audioFrame);
                    }
                }
                else if (SatelliteMessageReader.PeekType(frame.Value.Payload) == SatelliteProtocol.ClockPong)
                {
                    var pong = SatelliteMessageReader.Deserialize<SatelliteClockPongMessage>(frame.Value.Payload);
                    if (pong is not null)
                    {
                        var t3 = SatelliteClock.NowMs();
                        ClockSync.AddSample(new ClockRoundTrip(pong.T0, pong.T1, pong.T2, t3));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            IsConnected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ClockSyncLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await WriteControlAsync(new SatelliteClockPingMessage { T0 = SatelliteClock.NowMs() }, ct).ConfigureAwait(false);
                await Task.Delay(ClockSyncInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Sends one clock-sync ping immediately, outside the periodic loop — useful for
    /// tests/first-sync so callers don't have to wait out <see cref="ClockSyncInterval"/>.</summary>
    public Task PingClockAsync(CancellationToken ct = default) =>
        WriteControlAsync(new SatelliteClockPingMessage { T0 = SatelliteClock.NowMs() }, ct);

    private async Task WriteControlAsync<T>(T message, CancellationToken ct) where T : class
    {
        var payload = SatelliteMessageReader.Serialize(message);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SatelliteFraming.WriteAsync(_stream!, SatelliteFrameType.Control, payload, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();

        var pending = new List<Task>();
        if (_receiveLoopTask is not null)
        {
            pending.Add(_receiveLoopTask);
        }

        if (_clockSyncLoopTask is not null)
        {
            pending.Add(_clockSyncLoopTask);
        }

        if (pending.Count > 0)
        {
            try
            {
                await Task.WhenAll(pending).ConfigureAwait(false);
            }
            catch
            {
                // expected on cancellation
            }
        }

        _cts?.Dispose();
        _writeLock.Dispose();
        _tcpClient.Dispose();
    }
}
