using Spectralis.Core.Audio;
using Spectralis.Core.Common;

namespace Spectralis.App.ViewModels;

public sealed partial class NowPlayingViewModel
{
    private SessionQueueBookmark? sessionQueue;
    public bool SessionRoundActive => sessionQueue is not null;

    public async Task SuspendSessionQueueAsync()
    {
        if (sessionQueue is not null) return;
        if (IsMixSource) throw new InvalidOperationException("Switch from the DJ mix to track playback before starting a Song Wars round.");
        var spotifyUri = _spotifyState is { } spotify ? $"spotify:track:{spotify.TrackId}" : null;
        var bookmark = new SessionQueueBookmark(Queue.Items.ToArray(), Queue.CurrentIndex, spotifyUri is null ? _engine.GetPosition() : PositionSeconds,
            IsPlaying, Queue.Repeat, Queue.Shuffle, spotifyUri is null ? _engine.CurrentTrack : null,
            _remoteAudioTempPath, _engine.GaplessEnabled, _engine.CrossfadeSeconds, spotifyUri);
        if (spotifyUri is not null && _spotifyHost is not null) await _spotifyHost.PauseAsync();
        sessionQueue = bookmark;
        // The next load normally deletes the previous download; the interrupted queue still needs it.
        _remoteAudioTempPath = null;
        _engine.Pause();
        _engine.GaplessEnabled = false;
        _engine.CrossfadeSeconds = 0;
        Queue.Clear();
        Queue.Repeat = RepeatMode.None;
        Queue.Shuffle = false;
        SyncQueueItems();
    }

    public async Task RestoreSessionQueueAsync()
    {
        if (sessionQueue is not { } saved) return;
        _engine.Pause();
        if (saved.SpotifyUri is { } spotifyUri)
        {
            _spotifyStopRequested = false;
            if (_spotifyHost is null || !await _spotifyHost.PlayUriAsync(spotifyUri))
                throw new InvalidOperationException("Reconnect Spotify, then return to listening again.");
            await _spotifyHost.SeekAsync((int)(saved.Position * 1000));
            if (!saved.WasPlaying) await _spotifyHost.PauseAsync();
            Queue.Clear();
            Queue.AddRange(saved.Paths);
            if (saved.Paths.Length > 0) Queue.SetCurrent(saved.Index);
            SyncQueueItems();
        }
        else if (saved.Track is { } track && File.Exists(track.SourcePath))
        {
            await LoadPreparedTrackAsync(track.SourcePath, track, startPlayback: false, ownsTemporaryFile: false);
            if (!string.IsNullOrEmpty(LoadError)) throw new InvalidOperationException(LoadError);
            _remoteAudioTempPath = saved.TemporaryPath;
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
        TrackInfo? Track, string? TemporaryPath, bool Gapless, double Crossfade, string? SpotifyUri);
}
