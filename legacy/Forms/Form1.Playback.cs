using System.IO;

namespace Spectralis;

public partial class Form1
{
    private void SeekRelative(float deltaSec)
    {
        if (IsYouTubeActive)
        {
            _ = YouTubeSeekAsync(Math.Clamp(youTubePositionSeconds + deltaSec, 0, youTubeDurationSeconds));
            return;
        }

        if (IsSoundCloudActive)
        {
            _ = SoundCloudSeekAsync(Math.Clamp(soundCloudPositionSeconds + deltaSec, 0, soundCloudDurationSeconds));
            return;
        }

        if (IsSunoActive)
        {
            _ = SunoSeekAsync(Math.Clamp(sunoPositionSeconds + deltaSec, 0, sunoDurationSeconds));
            return;
        }

        if (IsSpotifyActive)
        {
            _ = SpotifySeekAsync(Math.Clamp(SpotifyCurrentPositionSeconds + deltaSec, 0, spotifyDurationSeconds));
            return;
        }

        if (!engine.IsLoaded)
            return;

        engine.Seek(Math.Clamp(engine.GetPosition() + deltaSec, 0, engine.GetLength()));
        NotifySharedPlayPlaybackChanged("seek");
        UpdateUiState();
    }

    private void AdjustVolume(int delta)
    {
        trackBarVolume.Value = Math.Clamp(trackBarVolume.Value + delta, 0, 100);
        engine.Volume = trackBarVolume.Value / 100f;
        if (IsYouTubeActive)
            _ = YouTubeSetVolumeAsync(trackBarVolume.Value / 100f);
        if (IsSunoActive)
            _ = SunoSetVolumeAsync(trackBarVolume.Value / 100f);
        UpdateUiState();
    }

    private void btnPlayPause_Click(object sender, EventArgs e)
    {
        if (IsYouTubeActive)
        {
            _ = YouTubePlayPauseAsync();
            return;
        }

        if (IsSoundCloudActive)
        {
            _ = SoundCloudPlayPauseAsync();
            return;
        }

        if (IsSunoActive)
        {
            _ = SunoPlayPauseAsync();
            return;
        }

        if (!engine.IsLoaded && queue.CurrentPath is { } queuedPath)
        {
            _ = PlayQueueItemAsync(queuedPath, startPlayback: true);
            return;
        }

        if (IsSpotifyActive || (!engine.IsLoaded && (spotifyDeviceId is not null || spotifyService.IsLinked)))
        {
            _ = SpotifyPlayPauseAsync();
            return;
        }

        if (!engine.IsLoaded)
        {
            OpenAudioFiles(startPlayback: true, addToQueue: false);
            return;
        }

        engine.Toggle();
        NotifySharedPlayPlaybackChanged(engine.IsPlaying ? "play" : "pause");
        UpdateUiState();
    }

    private void btnStop_Click(object sender, EventArgs e)
    {
        ResetPlaybackSession();
    }

    private void ResetPlaybackSession()
    {
        if (IsSoundCloudActive && soundCloudWebView?.CoreWebView2 is not null)
        {
            try { _ = soundCloudWebView.CoreWebView2.ExecuteScriptAsync("window.scPause && window.scPause()"); }
            catch { }
        }

        if (IsSpotifyActive)
            _ = spotifyService.PauseAsync(SpotifyClientId, spotifyDeviceId);

        StopJoinedSharedPlaySession(stopPlayback: false, clearStatus: true);
        sharedPlay.ClearActiveSession();
        _sharedPlayLinkPromptActive = false;
        _sharedPlayLastKnownJoinUrl = null;

        StopYouTubePlayback();
        StopSoundCloudPlayback();
        StopSunoPlayback();
        StopSpotifyPlayback();
        StopRemoteAudioPlayback();
        CancelSpotifyLocalHandoff();

        engine.Unload();
        if (IsAlbumWorldActive)
            UnloadAlbumCapsule();
        UnloadCapsule();

        queue.Clear();
        queue.Shuffle = false;
        queue.Repeat = RepeatMode.None;
        isQueueVisible = false;
        prevEngineIsPlaying = false;
        ctxQueueTargetIndex = -1;

        youTubeStatusMessage = null;
        soundCloudStatusMessage = null;
        sunoStatusMessage = null;
        joinedSharedPlayStatus = null;

        visualizerControl.ClearFrame();
        RemoteAudioCache.Clear();
        sharedPlay.ClearCache();
        sharedPlayJoinedPackageStore.Clear();

        SyncQueueControl();
        RefreshContentColumns();
        UpdateQueueModeButtons();
        UpdateUiState();
        UpdateSeekBar();
    }

    private void btnMute_Click(object sender, EventArgs e)
    {
        if (isMuted)
        {
            isMuted = false;
            trackBarVolume.Value = Math.Max(1, (int)(preMuteVolume * 100));
        }
        else
        {
            preMuteVolume = Math.Max(0.01f, trackBarVolume.Value / 100f);
            isMuted = true;
            trackBarVolume.Value = 0;
        }

        engine.Volume = trackBarVolume.Value / 100f;
        if (IsYouTubeActive)
            _ = YouTubeSetVolumeAsync(trackBarVolume.Value / 100f);
        if (IsSunoActive)
            _ = SunoSetVolumeAsync(trackBarVolume.Value / 100f);
        UpdateUiState();
    }

    private void btnDefaultApp_Click(object sender, EventArgs e)
    {
        try
        {
            DefaultAppRegistrar.RegisterCurrentUser();
            DefaultAppRegistrar.OpenDefaultAppsSettings();

            MessageBox.Show(
                this,
                "Spectralis has been registered for the current Windows user. Choose Spectralis in the Default Apps window that just opened to make it the handler for supported audio files. Shared Play app links are registered automatically.",
                "Default App Registration",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowError(
                $"Spectralis could not register itself for Windows file associations.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Registration Error");
        }
    }

    private void OpenAudioFile(bool startPlayback)
    {
        OpenAudioFiles(startPlayback, addToQueue: false);
    }

    private static bool IsCapsulePath(string path) =>
        string.Equals(Path.GetExtension(path), ".spectralis", StringComparison.OrdinalIgnoreCase);

    private static bool IsAlbumCapsulePath(string path) =>
        string.Equals(Path.GetExtension(path), ".spectral", StringComparison.OrdinalIgnoreCase);

    private Task HandleDroppedAudioFilesAsync(string[] files)
    {
        var validPaths = files.Where(File.Exists).ToArray();
        if (validPaths.Length == 0)
            return Task.CompletedTask;

        // Single .spectral album capsule drop
        if (validPaths.Length == 1 && IsAlbumCapsulePath(validPaths[0]))
            return OpenAlbumCapsuleAsync(validPaths[0]);

        // Single .spectralis capsule drop
        if (validPaths.Length == 1 && IsCapsulePath(validPaths[0]))
            return OpenCapsuleAsync(validPaths[0]);

        var audioPaths = validPaths.Where(p => !IsCapsulePath(p) && !IsAlbumCapsulePath(p)).ToArray();
        if (audioPaths.Length == 0)
            return Task.CompletedTask;

        if (appSettings.QueueByDefault)
        {
            if (IsSpotifyActive)
                StartLocalInterludeFromSpotify(audioPaths);
            else
                QueueLocalFiles(audioPaths, playIfQueueWasEmpty: appSettings.AutoPlayOnOpen);
        }
        else
        {
            var startPlayback = spotifyService.IsLinked || IsSpotifyReady || appSettings.AutoPlayOnOpen;
            LoadFilesAsQueue(audioPaths, startPlayback);
        }

        return Task.CompletedTask;
    }

    private static readonly string CapsuleOpenFilter =
        $"All Supported|{string.Join(";", SupportedAudioFormats.Extensions.Select(e => $"*{e}"))};*.spectralis;*.spectral" +
        $"|Spectralis Capsules|*.spectralis" +
        $"|Spectralis Album Worlds|*.spectral" +
        $"|{SupportedAudioFormats.OpenFileDialogFilter}";

    private void OpenAudioFiles(bool startPlayback, bool addToQueue)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = addToQueue ? SupportedAudioFormats.OpenFileDialogFilter : CapsuleOpenFilter,
            Title = addToQueue ? "Add files to queue" : "Select audio files",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var files = dialog.FileNames;

        if (addToQueue)
        {
            QueueLocalFiles(files.Where(File.Exists).ToArray(), playIfQueueWasEmpty: startPlayback);
            return;
        }

        // Album capsule case
        if (files.Length == 1 && IsAlbumCapsulePath(files[0]))
        {
            _ = OpenAlbumCapsuleAsync(files[0]);
            return;
        }

        // Capsule case — only handle the first file if it's a .spectralis
        if (files.Length == 1 && IsCapsulePath(files[0]))
        {
            _ = OpenCapsuleAsync(files[0]);
            return;
        }

        LoadFilesAsQueue(files.Where(f => !IsCapsulePath(f) && !IsAlbumCapsulePath(f)).ToArray(), startPlayback);
    }

    private void LoadFilesAsQueue(string[] paths, bool startPlayback)
    {
        var validPaths = paths.Where(File.Exists).ToArray();
        if (validPaths.Length == 0) return;

        if (IsYouTubeActive)
            StopYouTubePlayback();
        if (IsSoundCloudActive)
            StopSoundCloudPlayback();
        if (IsSunoActive)
            StopSunoPlayback();

        if (spotifyService.IsLinked)
            ParkSpotifyForLocalPlayback(resumeAfterLocalPlayback: false, advanceOnResume: false);
        else
            CancelSpotifyLocalHandoff();

        queue.Clear();
        queue.AddRange(validPaths);
        var firstPath = queue.SetCurrent(0)!;

        if (isQueueVisible)
        {
            SyncQueueControl();
            RefreshContentColumns();
        }

        LoadAudioFile(firstPath, startPlayback, fromQueue: true);
    }

    private void LoadAudioFile(string path, bool startPlayback, bool fromQueue = false)
    {
        StopJoinedSharedPlaySession(stopPlayback: false, clearStatus: true);
        if (IsYouTubeActive)
            StopYouTubePlayback();
        if (IsSoundCloudActive)
            StopSoundCloudPlayback();
        if (IsSunoActive)
            StopSunoPlayback();
        if (IsSpotifyActive)
            ParkSpotifyForLocalPlayback(resumeAfterLocalPlayback: false, advanceOnResume: false);
        StopRemoteAudioPlayback();
        if (IsAlbumWorldActive)
            UnloadAlbumCapsule();
        UnloadCapsule();

        if (!File.Exists(path))
        {
            ShowError(
                $"The selected file could not be found:{Environment.NewLine}{Environment.NewLine}{path}",
                "Open Error");
            return;
        }

        var loaded = false;

        try
        {
            engine.Load(path);
            LoadReactiveSidecar(path);
            visualizerControl.ClearFrame();
            trackBarSeek.Value = 0;
            loaded = true;
            OnLocalFileLoaded(path);
            OnScrobblingTrackLoaded(path);
            OnBeatGridTrackLoaded(path);
            OnKaraokeTrackLoaded(path);
        }
        catch (Exception ex)
        {
            ShowError(
                $"Unable to load the selected audio file.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Load Error");
        }

        if (loaded && startPlayback)
        {
            engine.Toggle();
            NotifySharedPlayPlaybackChanged(engine.IsPlaying ? "play" : "track-load");
        }
        else if (loaded)
        {
            NotifySharedPlayPlaybackChanged("track-load");
        }

        if (loaded && isQueueVisible)
            SyncQueueControl();

        UpdateUiState();
    }

    partial void OnLocalFileLoaded(string path);
    partial void OnScrobblingTrackLoaded(string path);
    partial void OnBeatGridTrackLoaded(string path);
    partial void OnKaraokeTrackLoaded(string path);
}
