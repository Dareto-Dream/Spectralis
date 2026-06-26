using Spectralis.App.Design;
using Spectralis.App.ViewModels;
using Spectralis.Core.Audio;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class NowPlayingViewModelTests : IDisposable
{
    private readonly FakeAudioDeviceEnumerator _devices = new();
    private readonly AudioEngine _engine;
    private readonly NowPlayingViewModel _vm;
    private readonly List<string> _tempFiles = new();

    public NowPlayingViewModelTests()
    {
        _engine = new AudioEngine(_devices);
        _vm = new NowPlayingViewModel(_engine, enablePositionPolling: false);
    }

    public void Dispose()
    {
        _vm.Dispose();
        _engine.Dispose();
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { }
        }
    }

    private string CreateWav(double seconds = 0.5)
    {
        var path = WavFixture.CreateSineWav(seconds);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public void InitialState_NoTrack()
    {
        Assert.False(_vm.HasTrack);
        Assert.False(_vm.IsPlaying);
        Assert.Equal("0:00", _vm.PositionText);
    }

    [Fact]
    public async Task LoadTrack_PopulatesIdentityAndStartsPlayback()
    {
        var path = CreateWav(seconds: 1.0);

        await _vm.LoadTrackAsync(path);

        Assert.True(_vm.HasTrack);
        Assert.True(_vm.IsPlaying);
        Assert.Equal(Path.GetFileNameWithoutExtension(path), _vm.Title);
        Assert.Contains("WAV", _vm.FormatBadge);
        Assert.Contains("kHz", _vm.FormatBadge);
        Assert.InRange(_vm.LengthSeconds, 0.9, 1.1);
        Assert.Equal("", _vm.LoadError);
    }

    [Fact]
    public async Task LoadTrack_BadFile_SurfacesErrorWithoutThrowing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "vm-missing-" + Guid.NewGuid().ToString("N") + ".wav");

        await _vm.LoadTrackAsync(missing);

        Assert.False(_vm.HasTrack);
        Assert.NotEqual("", _vm.LoadError);
    }

    [Fact]
    public async Task PlayPauseCommand_TogglesIconAndState()
    {
        await _vm.LoadTrackAsync(CreateWav());
        Assert.Equal(IconData.Pause, _vm.PlayPauseIconData);

        _vm.PlayPauseCommand.Execute().Subscribe();
        Assert.False(_vm.IsPlaying);
        Assert.Equal(IconData.Play, _vm.PlayPauseIconData);

        _vm.PlayPauseCommand.Execute().Subscribe();
        Assert.True(_vm.IsPlaying);
    }

    [Fact]
    public async Task StopCommand_ReturnsToEmptyPlayer()
    {
        await _vm.LoadTrackAsync(CreateWav(seconds: 1.0));
        _engine.Seek(0.8f);

        _vm.StopCommand.Execute().Subscribe();

        Assert.False(_vm.HasTrack);
        Assert.False(_vm.IsPlaying);
        Assert.Equal(0, _vm.PositionSeconds, 2);
        Assert.Equal(0, _vm.Queue.Count);
        Assert.Equal("", _vm.Title);
        Assert.Equal("", _vm.LoadError);
    }

    [Fact]
    public async Task SettingPositionFarFromEngine_Seeks()
    {
        await _vm.LoadTrackAsync(CreateWav(seconds: 2.0));

        _vm.PositionSeconds = 1.5;

        Assert.InRange(_engine.GetPosition(), 1.4f, 1.6f);
    }

    [Fact]
    public async Task VolumePercent_DrivesEngineVolume()
    {
        await _vm.LoadTrackAsync(CreateWav());

        _vm.VolumePercent = 40;

        Assert.Equal(0.4f, _engine.Volume, 2);
    }

    [Fact]
    public void SurfaceMode_TogglesVisualizerPeakAndArtwork()
    {
        _vm.UseArtworkSurface();

        Assert.Equal("OFF", _vm.SurfaceModeLabel);

        _vm.CycleSurfaceMode();

        Assert.True(_vm.IsSurfaceVisualizer);
        Assert.Equal("VIZ", _vm.SurfaceModeLabel);

        _vm.CycleSurfaceMode();

        Assert.True(_vm.IsSurfacePeak);
        Assert.Equal("PEAK", _vm.SurfaceModeLabel);

        _vm.CycleSurfaceMode();

        Assert.True(_vm.IsSurfaceOff);
        Assert.Equal("OFF", _vm.SurfaceModeLabel);

        _vm.UseVisualizerSurface();

        Assert.True(_vm.ShowVisualizer);
        Assert.False(_vm.PeakHold);
        Assert.True(_vm.IsSurfaceVisualizer);

        _vm.UsePeakSurface();

        Assert.True(_vm.ShowVisualizer);
        Assert.True(_vm.PeakHold);
        Assert.True(_vm.IsSurfacePeak);

        _vm.UseArtworkSurface();

        Assert.False(_vm.ShowVisualizer);
        Assert.False(_vm.ShowYouTubeVideo);
        Assert.True(_vm.IsSurfaceOff);
    }

    [Fact]
    public async Task QueueItems_MirrorQueueAndFlagCurrentRow()
    {
        var first = CreateWav();
        var second = CreateWav();

        await _vm.PlayQueueAsync([first, second], 0, startPlayback: false);

        Assert.Equal(2, _vm.QueueItems.Count);
        Assert.True(_vm.HasQueueItems);
        Assert.Equal("Queue - 2 tracks", _vm.QueueHeaderText);
        Assert.Equal("1 upcoming", _vm.QueueUpcomingText);
        Assert.True(_vm.QueueItems[0].IsCurrent);
        Assert.False(_vm.QueueItems[1].IsCurrent);

        await _vm.PlayNextAsync();

        Assert.False(_vm.QueueItems[0].IsCurrent);
        Assert.True(_vm.QueueItems[1].IsCurrent);
        Assert.Equal("0 upcoming", _vm.QueueUpcomingText);
    }

    [Fact]
    public async Task QueueMutations_ReorderRemoveAndClear()
    {
        var first = CreateWav();
        var second = CreateWav();
        var third = CreateWav();

        await _vm.PlayQueueAsync([first, second, third], 0, startPlayback: false);

        _vm.MoveQueueItemDown(_vm.QueueItems[1]);

        Assert.Equal(third, _vm.QueueItems[1].Path);
        Assert.Equal(second, _vm.QueueItems[2].Path);

        _vm.PlayQueueItemNext(_vm.QueueItems[2]);

        Assert.Equal(second, _vm.QueueItems[1].Path);
        Assert.True(_vm.QueueItems[0].IsCurrent);

        _vm.RemoveQueueItem(_vm.QueueItems[2]);

        Assert.Equal(2, _vm.QueueItems.Count);

        _vm.ClearQueue();

        Assert.Empty(_vm.QueueItems);
        Assert.False(_vm.HasQueueItems);
        Assert.False(_vm.IsPlaying);
    }

    [Fact]
    public async Task TimeLabelToggle_SwitchesElapsedAndRemaining()
    {
        var path = CreateWav(seconds: 1.0);
        await _vm.LoadTrackAsync(path);
        _vm.TogglePlayback();

        Assert.Equal("0:00", _vm.PositionText);

        _vm.ToggleTimeDisplay();

        Assert.StartsWith("-", _vm.PositionText);

        _vm.ToggleTimeDisplay();

        Assert.Equal("0:00", _vm.PositionText);
    }

    [Fact]
    public void TimeLabelToggle_WithoutTrack_StaysElapsed()
    {
        _vm.ToggleTimeDisplay();

        Assert.Equal("0:00", _vm.PositionText);
    }
}
