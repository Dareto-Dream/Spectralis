using System.Threading.Channels;
using Spectralis.Core.Audio;
using Spectralis.Core.Satellite;

namespace Spectralis.App.Services;

/// <summary>
/// Bridges <see cref="AudioEngine.RawAudioBlockCaptured"/> (the real post-EffectChain PCM tap)
/// to a <see cref="SatelliteSourceServer"/>'s outbound broadcast — the last piece connecting
/// the Satellite protocol to actual audio.
///
/// The capture callback fires synchronously on NAudio's playback thread, which must never
/// block on network I/O — so it only copies the block into a small bounded channel and
/// returns immediately (dropping the oldest queued block, never the newest, under backpressure:
/// a receiver momentarily behind should lose old audio, not have live audio wait behind it). A
/// background task drains the channel and does the actual async broadcast.
/// </summary>
public sealed class SatelliteAudioBroadcaster : IDisposable
{
    private readonly AudioEngine _engine;
    private readonly Channel<CapturedBlock> _channel;
    private readonly CancellationTokenSource _cts = new();
    private Task? _pumpTask;
    private SatelliteSourceServer? _server;

    public SatelliteAudioBroadcaster(AudioEngine engine)
    {
        _engine = engine;
        _channel = Channel.CreateBounded<CapturedBlock>(new BoundedChannelOptions(4)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
        });
    }

    public bool IsAttached => _server is not null;

    /// <summary>Starts tapping the engine and broadcasting to <paramref name="server"/>. Safe to
    /// call again with a different server to switch targets (e.g. after a restart).</summary>
    public void Attach(SatelliteSourceServer server)
    {
        _server = server;
        _engine.RawAudioBlockCaptured = OnRawBlockCaptured;
        _pumpTask ??= Task.Run(() => PumpAsync(_cts.Token));
    }

    /// <summary>Stops broadcasting — the engine keeps playing normally, it just stops feeding
    /// this tap. The pump task keeps running (idle) so re-<see cref="Attach"/> doesn't need to
    /// re-spin it.</summary>
    public void Detach()
    {
        _server = null;
        _engine.RawAudioBlockCaptured = null;
    }

    private void OnRawBlockCaptured(float[] buffer, int offset, int samplesRead, int channels)
    {
        if (samplesRead <= 0 || _server is null)
        {
            return;
        }

        // Must copy — the audio pipeline reuses/overwrites this buffer on the very next Read.
        var copy = new float[samplesRead];
        Array.Copy(buffer, offset, copy, 0, samplesRead);
        _channel.Writer.TryWrite(new CapturedBlock(copy, channels, _engine.EffectiveSampleRate));
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var block in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var server = _server;
                if (server is null)
                {
                    continue;
                }

                // Only bother computing FFT bins if at least one connected receiver actually
                // wants them — a receive-only speaker's frames get sent without this work.
                var anyWantsFft = server.ConnectedReceivers.Any(r => r.Display != SatelliteDisplay.None);
                var fftBins = anyWantsFft
                    ? _engine.GetVisualizerFrame(includeRawFft: true).RawFftBins
                    : [];

                var frame = new SatelliteAudioFrame
                {
                    SourceClockMs = SatelliteClock.NowMs(),
                    SampleRate = block.SampleRate,
                    ChannelCount = block.Channels,
                    PcmSamples = block.Samples,
                    FftBins = fftBins,
                };

                try
                {
                    await server.BroadcastAudioFrameAsync(frame, ct).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // One bad broadcast (e.g. every receiver mid-disconnect) shouldn't kill the
                    // pump loop — the next captured block gets a fresh attempt.
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        Detach();
        _cts.Cancel();
        _channel.Writer.TryComplete();
        _cts.Dispose();
    }

    private readonly record struct CapturedBlock(float[] Samples, int Channels, int SampleRate);
}
