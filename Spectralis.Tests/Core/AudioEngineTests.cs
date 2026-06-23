using Spectralis.Core.Audio;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class AudioEngineTests : IDisposable
{
    private readonly FakeAudioDeviceEnumerator _devices = new();
    private readonly AudioEngine _engine;
    private readonly List<string> _tempFiles = new();

    public AudioEngineTests()
    {
        _engine = new AudioEngine(_devices);
    }

    public void Dispose()
    {
        _engine.Dispose();
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

    [Fact]
    public void LoadWav_PopulatesTrackInfoAndStopsReady()
    {
        var path = CreateWav(seconds: 1.0, sampleRate: 44100, channels: 2);

        _engine.Load(path);

        Assert.True(_engine.IsLoaded);
        Assert.Equal(PlaybackState.Stopped, _engine.StateMachine.State);
        Assert.NotNull(_engine.CurrentTrack);
        Assert.Equal("WAV", _engine.CurrentTrack!.FormatName);
        Assert.Equal(2, _engine.CurrentTrack.Channels);
        Assert.Equal(44100, _engine.CurrentTrack.SampleRateHz);
        Assert.True(_engine.CurrentTrack.FileSizeBytes > 0);
        Assert.InRange(_engine.GetLength(), 0.9f, 1.1f);
    }

    [Fact]
    public void LoadMissingFile_ThrowsAndEntersErrorState()
    {
        var missing = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".wav");

        Assert.ThrowsAny<Exception>(() => _engine.Load(missing));
        Assert.Equal(PlaybackState.Error, _engine.StateMachine.State);
        Assert.False(_engine.IsLoaded);
    }

    [Fact]
    public void PlayPauseStop_DriveStateMachineAndDevice()
    {
        _engine.Load(CreateWav());

        _engine.Play();
        Assert.Equal(PlaybackState.Playing, _engine.StateMachine.State);
        Assert.True(_devices.Current!.IsPlaying);

        _engine.Pause();
        Assert.Equal(PlaybackState.Paused, _engine.StateMachine.State);
        Assert.False(_devices.Current!.IsPlaying);

        _engine.Play();
        Assert.Equal(PlaybackState.Playing, _engine.StateMachine.State);

        _engine.Stop();
        Assert.Equal(PlaybackState.Stopped, _engine.StateMachine.State);
        Assert.Equal(0f, _engine.GetPosition());
    }

    [Fact]
    public void Toggle_AlternatesPlayAndPause()
    {
        _engine.Load(CreateWav());

        _engine.Toggle();
        Assert.Equal(PlaybackState.Playing, _engine.StateMachine.State);
        _engine.Toggle();
        Assert.Equal(PlaybackState.Paused, _engine.StateMachine.State);
    }

    [Fact]
    public void Seek_ClampsToTrackBounds()
    {
        _engine.Load(CreateWav(seconds: 1.0));

        _engine.Seek(0.5f);
        Assert.InRange(_engine.GetPosition(), 0.45f, 0.55f);

        _engine.Seek(99f);
        Assert.True(_engine.GetPosition() <= _engine.GetLength() + 0.01f);

        _engine.Seek(-5f);
        Assert.Equal(0f, _engine.GetPosition());
    }

    [Fact]
    public void Volume_ClampsAndPropagatesToDevice()
    {
        _engine.Load(CreateWav());

        _engine.Volume = 1.7f;
        Assert.Equal(1f, _engine.Volume);
        Assert.Equal(1f, _devices.Current!.Volume);

        _engine.Volume = -0.3f;
        Assert.Equal(0f, _engine.Volume);
        Assert.Equal(0f, _devices.Current!.Volume);
    }

    [Fact]
    public void NaturalEndOfStream_RaisesTrackEndedAndStops()
    {
        _engine.Load(CreateWav(seconds: 0.2));
        var ended = 0;
        _engine.TrackEnded += (_, _) => ended++;

        _engine.Play();
        _devices.Current!.DrainSource();

        Assert.Equal(1, ended);
        Assert.Equal(PlaybackState.Stopped, _engine.StateMachine.State);
    }

    [Fact]
    public void StopDoesNotRaiseTrackEnded()
    {
        _engine.Load(CreateWav());
        var ended = 0;
        _engine.TrackEnded += (_, _) => ended++;

        _engine.Play();
        _engine.Stop();

        Assert.Equal(0, ended);
    }

    [Fact]
    public void DeviceFailure_RebuildsOutputChain()
    {
        _engine.Load(CreateWav());
        _engine.Play();
        var firstDevice = _devices.Current!;

        firstDevice.FailPlayback(new InvalidOperationException("device lost"));

        // Recovery creates a fresh device and resumes; engine never surfaces Error.
        Assert.True(_devices.CreatedDevices.Count >= 2);
        Assert.NotSame(firstDevice, _devices.Current);
        Assert.NotEqual(PlaybackState.Error, _engine.StateMachine.State);
    }

    [Fact]
    public void Unload_ReturnsToIdle()
    {
        _engine.Load(CreateWav());
        _engine.Play();

        _engine.Unload();

        Assert.False(_engine.IsLoaded);
        Assert.Equal(PlaybackState.Idle, _engine.StateMachine.State);
        Assert.Null(_engine.CurrentTrack);
    }

    [Fact]
    public void PreferredSampleRate_ResamplesOutput()
    {
        _engine.Load(CreateWav(sampleRate: 44100));
        _engine.SetPreferredSampleRate(48000);

        Assert.Equal(48000, _engine.EffectiveSampleRate);
    }

    [Fact]
    public void VisualizerFrame_AvailableWhileLoaded()
    {
        _engine.Load(CreateWav());
        var frame = _engine.GetVisualizerFrame();
        Assert.NotNull(frame);
        Assert.NotNull(frame.Spectrum);
    }
}
