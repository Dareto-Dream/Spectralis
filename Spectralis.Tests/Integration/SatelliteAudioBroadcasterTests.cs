using Spectralis.App.Services;
using Spectralis.Core.Audio;
using Spectralis.Core.Satellite;
using Spectralis.Tests.Core;
using System.Collections.Concurrent;
using Xunit;

namespace Spectralis.Tests.Integration;

/// <summary>
/// The full Satellite chain, end to end: a real <see cref="AudioEngine"/> actually playing a
/// WAV file, through the real post-EffectChain tap, through a real
/// <see cref="SatelliteAudioBroadcaster"/>, over a real loopback TCP connection, to a real
/// <see cref="SatelliteReceiverClient"/>. Nothing here is mocked except the audio *device*
/// (FakeAudioDevice — no real speakers involved) and the WAV file's content.
/// </summary>
public sealed class SatelliteAudioBroadcasterTests : IAsyncLifetime
{
    private readonly FakeAudioDeviceEnumerator _devices = new();
    private readonly AudioEngine _engine;
    private readonly SatelliteSourceServer _server;
    private readonly SatellitePairedDevicesStore _pairedDevices;
    private readonly string _pairedDevicesPath = Path.Combine(Path.GetTempPath(), $"satellite-audio-paired-{Guid.NewGuid():N}.json");
    private readonly List<string> _tempFiles = [];
    private SatelliteAudioBroadcaster? _broadcaster;

    public SatelliteAudioBroadcasterTests()
    {
        _engine = new AudioEngine(_devices);
        _pairedDevices = new SatellitePairedDevicesStore(_pairedDevicesPath);
        _server = new SatelliteSourceServer(_pairedDevices, port: 0);
    }

    public Task InitializeAsync()
    {
        _server.Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _broadcaster?.Dispose();
        await _server.DisposeAsync();
        _engine.Dispose();
        if (File.Exists(_pairedDevicesPath))
        {
            File.Delete(_pairedDevicesPath);
        }

        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { }
        }
    }

    private string CreateWav(double seconds = 0.5, int sampleRate = 44100, int channels = 2)
    {
        var path = WavFixture.CreateSineWav(seconds, sampleRate, channels);
        _tempFiles.Add(path);
        return path;
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

    [Fact]
    public async Task RealPlayback_ReachesAReceiverOverARealSocket_WithRealAudioData()
    {
        _pairedDevices.MarkPaired("audio-device-1", "Test Speaker");
        _broadcaster = new SatelliteAudioBroadcaster(_engine);
        _broadcaster.Attach(_server);

        await using var client = new SatelliteReceiverClient();
        var connected = await client.ConnectAsync("127.0.0.1", _server.Port, "audio-device-1", "Test Speaker",
            SatelliteCodec.Pcm, SatelliteDisplay.Fft, obtainPin: null).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(connected);
        await WaitUntilAsync(() => _server.ConnectedReceivers.Count == 1);

        var receivedFrames = new ConcurrentQueue<SatelliteAudioFrame>();
        client.AudioFrameReceived += (_, frame) => receivedFrames.Enqueue(frame);

        _engine.Load(CreateWav(seconds: 0.3, sampleRate: 44100, channels: 2));
        _engine.Play();
        _devices.Current!.DrainSource(); // synchronously pulls the whole chain through the tap

        await WaitUntilAsync(() => receivedFrames.Count > 0, timeoutMs: 10000);

        Assert.NotEmpty(receivedFrames);
        Assert.All(receivedFrames, f => Assert.Equal(2, f.ChannelCount));
        Assert.All(receivedFrames, f => Assert.Equal(44100, f.SampleRate));
        Assert.Contains(receivedFrames, f => f.PcmSamples.Any(s => s != 0f)); // real sine data, not silence
        // This receiver declared Fft — it should actually get bins, proving GetVisualizerFrame
        // was wired in (not just PCM passthrough).
        Assert.Contains(receivedFrames, f => f.FftBins.Length > 0);
    }

    [Fact]
    public async Task DisplayNoneReceiver_GetsAudioWithoutFftOverTheRealPipeline()
    {
        _pairedDevices.MarkPaired("audio-device-2", "Speaker Only");
        _broadcaster = new SatelliteAudioBroadcaster(_engine);
        _broadcaster.Attach(_server);

        await using var client = new SatelliteReceiverClient();
        var connected = await client.ConnectAsync("127.0.0.1", _server.Port, "audio-device-2", "Speaker Only",
            SatelliteCodec.Pcm, SatelliteDisplay.None, obtainPin: null).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(connected);
        await WaitUntilAsync(() => _server.ConnectedReceivers.Count == 1);

        var receivedFrames = new ConcurrentQueue<SatelliteAudioFrame>();
        client.AudioFrameReceived += (_, frame) => receivedFrames.Enqueue(frame);

        _engine.Load(CreateWav(seconds: 0.3));
        _engine.Play();
        _devices.Current!.DrainSource();

        await WaitUntilAsync(() => receivedFrames.Count > 0, timeoutMs: 10000);

        Assert.All(receivedFrames, f => Assert.Empty(f.FftBins));
        Assert.Contains(receivedFrames, f => f.PcmSamples.Length > 0);
    }

    [Fact]
    public async Task Detach_StopsFeedingNewFramesToReceiver()
    {
        _pairedDevices.MarkPaired("audio-device-3", "Test Speaker");
        _broadcaster = new SatelliteAudioBroadcaster(_engine);
        _broadcaster.Attach(_server);

        await using var client = new SatelliteReceiverClient();
        var connected = await client.ConnectAsync("127.0.0.1", _server.Port, "audio-device-3", "Test Speaker",
            SatelliteCodec.Pcm, SatelliteDisplay.None, obtainPin: null).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(connected);
        await WaitUntilAsync(() => _server.ConnectedReceivers.Count == 1);

        _broadcaster.Detach();
        Assert.False(_broadcaster.IsAttached);

        var receivedAfterDetach = 0;
        client.AudioFrameReceived += (_, _) => receivedAfterDetach++;

        _engine.Load(CreateWav(seconds: 0.2));
        _engine.Play();
        _devices.Current!.DrainSource();

        await Task.Delay(300); // give any (unwanted) frame time to arrive
        Assert.Equal(0, receivedAfterDetach);
    }
}
