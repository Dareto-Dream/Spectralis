namespace Spectralis;

public partial class Form1
{
    private void InitializeNowPlaying()
    {
        nowPlaying.CommandRequested -= NowPlaying_CommandRequested;
        nowPlaying.CommandRequested += NowPlaying_CommandRequested;
        nowPlaying.SeekRequested -= NowPlaying_SeekRequested;
        nowPlaying.SeekRequested += NowPlaying_SeekRequested;
        nowPlaying.Initialize(Handle);
        SyncNowPlayingState();
    }

    private void SyncNowPlayingState()
    {
        var track = IsSpotifyActive ? spotifyCurrentTrack : IsSunoActive ? sunoCurrentTrack : engine.CurrentTrack;
        var isPlaying = IsSpotifyActive ? spotifyIsPlaying : IsSunoActive ? sunoIsPlaying : engine.IsPlaying;
        var position = IsSpotifyActive ? spotifyPositionSeconds : IsSunoActive ? sunoPositionSeconds : engine.GetPosition();
        var duration = IsSpotifyActive ? spotifyDurationSeconds : IsSunoActive ? sunoDurationSeconds : engine.GetLength();

        nowPlaying.Update(
            track,
            isPlaying,
            TimeSpan.FromSeconds(position),
            TimeSpan.FromSeconds(duration));
    }

    private void NowPlaying_CommandRequested(object? sender, WindowsNowPlayingCommand command)
    {
        if (!IsHandleCreated || IsDisposed || Disposing)
        {
            return;
        }

        BeginInvoke(new Action(() =>
        {
            switch (command)
            {
                case WindowsNowPlayingCommand.Play:
                    if (IsSpotifyActive)
                    {
                        _ = SpotifyPlayPauseAsync();
                        break;
                    }

                    if (IsSunoActive)
                    {
                        _ = SunoPlayPauseAsync();
                        break;
                    }

                    if (engine.IsLoaded && !engine.IsPlaying)
                    {
                        engine.Toggle();
                        NotifySharedPlayPlaybackChanged("play");
                        UpdateUiState();
                    }
                    break;

                case WindowsNowPlayingCommand.Pause:
                    if (IsSpotifyActive)
                    {
                        _ = SpotifyPlayPauseAsync();
                        break;
                    }

                    if (IsSunoActive)
                    {
                        _ = SunoPlayPauseAsync();
                        break;
                    }

                    if (engine.IsLoaded && engine.IsPlaying)
                    {
                        engine.Toggle();
                        NotifySharedPlayPlaybackChanged("pause");
                        UpdateUiState();
                    }
                    break;

                case WindowsNowPlayingCommand.Stop:
                    ResetPlaybackSession();
                    break;

                case WindowsNowPlayingCommand.Next:
                    if (IsSpotifyActive || queue.HasNext || resumeSpotifyAfterLocalPlayback) NavigateNext();
                    break;

                case WindowsNowPlayingCommand.Previous:
                    NavigatePrevious();
                    break;
            }
        }));
    }

    private void NowPlaying_SeekRequested(object? sender, TimeSpan position)
    {
        if (!IsHandleCreated || IsDisposed || Disposing)
        {
            return;
        }

        BeginInvoke(new Action(() =>
        {
            if (IsSpotifyActive)
            {
                _ = SpotifySeekAsync((float)position.TotalSeconds);
            }
            else if (IsSunoActive)
            {
                _ = SunoSeekAsync((float)position.TotalSeconds);
            }
            else if (engine.IsLoaded)
            {
                engine.Seek((float)position.TotalSeconds);
                NotifySharedPlayPlaybackChanged("seek");
                UpdateUiState();
            }
        }));
    }
}
