using Spectralis.Core.Audio;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class AudioEngineTransitionTests : IDisposable
{
    private const int Rate = 8000;

    private readonly FakeAudioDeviceEnumerator _devices = new();
    private readonly AudioEngine _engine;
    private readonly List<string> _tempFiles = [];

    public AudioEngineTransitionTests() => _engine = new AudioEngine(_devices);

    public void Dispose()
    {
        _engine.Dispose();
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { }
        }
    }

    private string Wav(double seconds)
    {
        var path = WavFixture.CreateSineWav(seconds, Rate, 2);
        _tempFiles.Add(path);
        return path;
    }

    // Starts `first` playing with `second` prepared (and so armed), then reads the device
    // until `seconds` of audio have come out, returning the samples.
    private async Task<float[]> PlayIntoNext(string first, string second, double seconds)
    {
        _engine.Load(first);
        _engine.Play();
        await _engine.PrepareNextAsync(second);

        var device = _devices.Current!;
        var all = new List<float>();
        var target = (int)(seconds * Rate * 2);
        while (all.Count < target)
        {
            var chunk = device.Pump(512);
            if (chunk.Length == 0)
                break;
            all.AddRange(chunk);
        }
        return all.ToArray();
    }

    [Fact]
    public async Task Gapless_moves_to_the_next_track_without_stopping_the_device()
    {
        var a = Wav(1.0);
        var b = Wav(1.0);
        string? transitionedTo = null;
        _engine.TrackTransitioned += (_, path) => transitionedTo = path;

        var audio = await PlayIntoNext(a, b, 1.6);

        Assert.Equal(b, transitionedTo);
        Assert.Equal(b, _engine.CurrentTrack!.SourcePath);
        Assert.Single(_devices.CreatedDevices);
        Assert.True(_devices.Current!.IsPlaying);
        // Nothing but audio came out for the whole 1.6s (no zero-length gap, no early end).
        Assert.Equal((int)(1.6 * Rate * 2), audio.Length);
    }

    [Fact]
    public async Task Crossfade_switches_current_track_when_the_overlap_starts()
    {
        _engine.CrossfadeSeconds = 0.5;
        var a = Wav(2.0);
        var b = Wav(2.0);
        string? transitionedTo = null;
        _engine.TrackTransitioned += (_, path) => transitionedTo = path;

        // 1.2s in: the first track still has 0.8s left, so no overlap yet.
        await PlayIntoNext(a, b, 1.2);
        Assert.Null(transitionedTo);
        Assert.Equal(a, _engine.CurrentTrack!.SourcePath);

        // 512 interleaved stereo samples = 32ms, so 16 pumps is ~0.5s more: track a then has
        // ~0.3s left, inside the 0.5s overlap.
        var device = _devices.Current!;
        for (var i = 0; i < 16; i++)
            device.Pump(512);

        Assert.Equal(b, transitionedTo);
        Assert.Equal(b, _engine.CurrentTrack!.SourcePath);
        Assert.True(_engine.IsPlaying);
    }

    [Fact]
    public async Task TrySeamlessAdvance_after_an_in_chain_transition_does_not_touch_the_device()
    {
        var a = Wav(1.0);
        var b = Wav(1.0);
        await PlayIntoNext(a, b, 1.2);
        var device = _devices.Current!;
        var inits = device.InitCount;

        var advanced = _engine.TrySeamlessAdvance(b);

        Assert.True(advanced);
        Assert.Equal(inits, device.InitCount);
        Assert.Equal(b, _engine.CurrentTrack!.SourcePath);
        // The claim is single-use: a later request for the same path swaps normally.
        Assert.True(_engine.TrySeamlessAdvance(b));
        Assert.True(device.InitCount > inits);
    }

    [Fact]
    public async Task Turning_both_off_falls_back_to_the_normal_advance()
    {
        _engine.GaplessEnabled = false;
        var a = Wav(0.5);
        var b = Wav(0.5);
        string? transitionedTo = null;
        _engine.TrackTransitioned += (_, path) => transitionedTo = path;

        var audio = await PlayIntoNext(a, b, 1.0);

        Assert.Null(transitionedTo);
        Assert.Equal(a, _engine.CurrentTrack!.SourcePath);
        Assert.True(audio.Length < (int)(0.6 * Rate * 2));
    }

    [Fact]
    public async Task Mono_next_track_is_upmixed_into_a_stereo_chain()
    {
        var a = Wav(0.5);
        var monoPath = WavFixture.CreateSineWav(0.5, Rate, 1);
        _tempFiles.Add(monoPath);

        var audio = await PlayIntoNext(a, monoPath, 0.9);

        Assert.Equal(monoPath, _engine.CurrentTrack!.SourcePath);
        // Reads come in 512-sample chunks, so allow the final chunk to overshoot the target.
        Assert.InRange(audio.Length, (int)(0.9 * Rate * 2), (int)(0.9 * Rate * 2) + 512);
    }
}
