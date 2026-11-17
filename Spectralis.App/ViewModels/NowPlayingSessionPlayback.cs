using Spectralis.Core.Audio;
using Spectralis.Core.Common;

namespace Spectralis.App.ViewModels;

public sealed partial class NowPlayingViewModel
{
    private SessionQueueBookmark? sessionQueue;
    public bool SessionRoundActive => sessionQueue is not null;

    public Task SuspendSessionQueueAsync()
    {
        if (sessionQueue is not null) return Task.CompletedTask;
        sessionQueue = new(Queue.Items.ToArray(), Queue.CurrentIndex, _engine.GetPosition(), IsPlaying, Queue.Repeat, Queue.Shuffle,
            _engine.CurrentTrack, _remoteAudioTempPath, _engine.GaplessEnabled, _engine.CrossfadeSeconds);
        // The next load normally deletes the previous download; the interrupted queue still needs it.
        _remoteAudioTempPath = null;
        _engine.Pause();
        _engine.GaplessEnabled = false;
        _engine.CrossfadeSeconds = 0;
        Queue.Clear();
        Queue.Repeat = RepeatMode.None;
        Queue.Shuffle = false;
        SyncQueueItems();
        return Task.CompletedTask;
    }

    public async Task RestoreSessionQueueAsync()
    {
        if (sessionQueue is not { } saved) return;
        _engine.Pause();
        if (saved.Track is { } track && File.Exists(track.SourcePath))
        {
            await LoadPreparedTrackAsync(track.SourcePath, track, startPlayback: false, ownsTemporaryFile: saved.TemporaryPath is not null);
            if (!string.IsNullOrEmpty(LoadError)) throw new InvalidOperationException(LoadError);
            Queue.Clear();
            Queue.AddRange(saved.Paths);
            if (saved.Paths.Length > 0) Queue.SetCurrent(saved.Index);
            SyncQueueItems();
            _engine.Seek((float)saved.Position);
            PositionSeconds = saved.Position;
            if (saved.WasPlaying) TogglePlayback();
        }
        else if (saved.Paths.Length > 0)
        {
            await PlayQueueAsync(saved.Paths, saved.Index, startPlayback: false);
            if (!string.IsNullOrEmpty(LoadError)) throw new InvalidOperationException(LoadError);
            _engine.Seek((float)saved.Position);
            PositionSeconds = saved.Position;
            if (saved.WasPlaying) TogglePlayback();
        }
        else { Queue.Clear(); SyncQueueItems(); }
        Queue.Repeat = saved.Repeat;
        Queue.Shuffle = saved.Shuffle;
        _engine.GaplessEnabled = saved.Gapless;
        _engine.CrossfadeSeconds = saved.Crossfade;
        sessionQueue = null;
    }

    private sealed record SessionQueueBookmark(string[] Paths, int Index, double Position, bool WasPlaying, RepeatMode Repeat, bool Shuffle,
        TrackInfo? Track, string? TemporaryPath, bool Gapless, double Crossfade);
}
