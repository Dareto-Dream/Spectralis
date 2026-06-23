using Spectralis.Core.Audio;
using Xunit;

namespace Spectralis.Tests.Core;

public class PlayQueueTests
{
    private static PlayQueue Loaded(params string[] items)
    {
        var queue = new PlayQueue();
        queue.AddRange(items);
        queue.SetCurrent(0);
        return queue;
    }

    [Fact]
    public void SequentialNavigation_WalksTheList()
    {
        var queue = Loaded("a", "b", "c");

        Assert.Equal("a", queue.CurrentPath);
        Assert.Equal("b", queue.MoveNext());
        Assert.Equal("c", queue.MoveNext());
        Assert.Null(queue.MoveNext()); // end, repeat off
        Assert.Equal("b", queue.MovePrevious());
    }

    [Fact]
    public void RepeatAll_WrapsBothDirections()
    {
        var queue = Loaded("a", "b");
        queue.Repeat = RepeatMode.All;

        queue.MoveNext();             // b
        Assert.Equal("a", queue.MoveNext()); // wraps

        queue.SetCurrent(0);
        Assert.Equal("b", queue.MovePrevious()); // wraps backwards
    }

    [Fact]
    public void RepeatOne_StaysOnCurrent()
    {
        var queue = Loaded("a", "b");
        queue.Repeat = RepeatMode.One;

        Assert.Equal("a", queue.MoveNext());
        Assert.Equal("a", queue.MovePrevious());
    }

    [Fact]
    public void Shuffle_VisitsEveryItemOnce()
    {
        var queue = Loaded("a", "b", "c", "d", "e");
        queue.Shuffle = true;

        var visited = new List<string> { queue.CurrentPath! };
        while (queue.MoveNext() is { } path)
        {
            visited.Add(path);
        }

        Assert.Equal(5, visited.Count);
        Assert.Equal(new[] { "a", "b", "c", "d", "e" }, visited.OrderBy(x => x));
    }

    [Fact]
    public void Remove_BeforeCurrent_AdjustsIndex()
    {
        var queue = Loaded("a", "b", "c");
        queue.SetCurrent(2);

        queue.Remove(0);

        Assert.Equal("c", queue.CurrentPath);
        Assert.Equal(1, queue.CurrentIndex);
    }

    [Fact]
    public void InsertRange_BeforeCurrent_ShiftsIndex()
    {
        var queue = Loaded("a", "b");
        queue.SetCurrent(1);

        queue.InsertRange(0, new[] { "x", "y" });

        Assert.Equal("b", queue.CurrentPath);
        Assert.Equal(3, queue.CurrentIndex);
    }

    [Fact]
    public void MoveUpDown_TracksCurrentItem()
    {
        var queue = Loaded("a", "b", "c");
        queue.SetCurrent(1); // b

        queue.MoveUp(1);     // b now at 0
        Assert.Equal("b", queue.CurrentPath);
        Assert.Equal(0, queue.CurrentIndex);

        queue.MoveDown(0);   // b back to 1
        Assert.Equal("b", queue.CurrentPath);
        Assert.Equal(1, queue.CurrentIndex);
    }

    [Fact]
    public void Clear_EmptiesEverything()
    {
        var queue = Loaded("a");
        queue.Clear();

        Assert.True(queue.IsEmpty);
        Assert.Null(queue.CurrentPath);
        Assert.False(queue.HasNext);
        Assert.False(queue.HasPrevious);
    }
}

public sealed class QueuePlaybackFlowTests : IDisposable
{
    private readonly FakeAudioDeviceEnumerator _devices = new();
    private readonly Spectralis.App.ViewModels.NowPlayingViewModel _vm;
    private readonly AudioEngine _engine;
    private readonly List<string> _tempFiles = new();

    public QueuePlaybackFlowTests()
    {
        _engine = new AudioEngine(_devices);
        _vm = new Spectralis.App.ViewModels.NowPlayingViewModel(_engine, enablePositionPolling: false);
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

    private string Wav()
    {
        var path = WavFixture.CreateSineWav(0.2);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public async Task PlayQueue_StartsAtRequestedIndexAndNavigates()
    {
        var paths = new[] { Wav(), Wav(), Wav() };

        await _vm.PlayQueueAsync(paths, 1);

        Assert.True(_vm.IsPlaying);
        Assert.Equal(paths[1], _engine.CurrentTrack!.SourcePath);
        Assert.True(_vm.HasNext);
        Assert.True(_vm.HasPrevious);

        _vm.NextCommand.Execute().Subscribe();
        await WaitForTrackAsync(paths[2]);
        Assert.False(_vm.HasNext);
    }

    [Fact]
    public async Task SingleFileLoad_ReplacesQueue()
    {
        await _vm.PlayQueueAsync(new[] { Wav(), Wav() }, 0);
        var single = Wav();

        await _vm.LoadTrackAsync(single);

        Assert.Equal(single, _engine.CurrentTrack!.SourcePath);
        Assert.Equal(1, _vm.Queue.Count);
        Assert.False(_vm.HasNext);
    }

    private async Task WaitForTrackAsync(string expectedPath)
    {
        for (var i = 0; i < 100; i++)
        {
            if (_engine.CurrentTrack?.SourcePath == expectedPath)
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Equal(expectedPath, _engine.CurrentTrack?.SourcePath);
    }
}
