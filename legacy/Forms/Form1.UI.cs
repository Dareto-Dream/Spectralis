namespace Spectralis;

public partial class Form1
{
    private void UpdateSeekBar()
    {
        float length, position;
        bool isActive;

        if (IsYouTubeActive)
        {
            length = youTubeDurationSeconds;
            position = youTubePositionSeconds;
            isActive = true;
        }
        else if (IsSoundCloudActive)
        {
            length = soundCloudDurationSeconds;
            position = soundCloudPositionSeconds;
            isActive = true;
        }
        else if (IsSunoActive)
        {
            length = sunoDurationSeconds;
            position = sunoPositionSeconds;
            isActive = true;
        }
        else if (IsSpotifyActive)
        {
            length = spotifyDurationSeconds;
            position = SpotifyCurrentPositionSeconds;
            isActive = true;
        }
        else
        {
            length = engine.GetLength();
            position = engine.GetPosition();
            isActive = engine.IsLoaded;
        }

        isUpdatingSeekBar = true;
        trackBarSeek.Maximum = Math.Max(1, (int)Math.Ceiling(Math.Max(length, 1)));
        trackBarSeek.Value = isActive
            ? Math.Clamp((int)Math.Floor(position), trackBarSeek.Minimum, trackBarSeek.Maximum)
            : 0;
        isUpdatingSeekBar = false;

        if (showRemainingTime && isActive)
            lblCurrentTime.Text = $"-{FormatTime(Math.Max(0, length - position))}";
        else
            lblCurrentTime.Text = FormatTime(position);

        lblDuration.Text = FormatTime(length);
    }

    private void UpdateVisualizer()
    {
        VisualizerFrame frame;
        bool isPlaying;
        float position;

        if (IsYouTubeActive)
        {
            frame = GetYouTubeVisualizerFrame();
            isPlaying = youTubeIsPlaying;
            position = youTubePositionSeconds;
        }
        else if (IsSoundCloudActive)
        {
            frame = GetSoundCloudVisualizerFrame();
            isPlaying = soundCloudIsPlaying;
            position = soundCloudPositionSeconds;
        }
        else if (IsSunoActive)
        {
            frame = GetSunoVisualizerFrame();
            isPlaying = sunoIsPlaying;
            position = sunoPositionSeconds;
        }
        else if (IsSpotifyActive)
        {
            frame = GetSpotifyVisualizerFrame();
            isPlaying = spotifyIsPlaying;
            position = SpotifyCurrentPositionSeconds;
        }
        else
        {
            frame = engine.GetVisualizerFrame();
            isPlaying = engine.IsPlaying;
            position = engine.GetPosition();
        }
        visualizerControl.UpdateFrame(frame, isPlaying, position);

        if (embeddedContentControl is not { HasContent: true } embeddedContent)
        {
            return;
        }

        if (embeddedContent.CanSyncVideo)
            _ = embeddedContent.SyncVideoPosition(position);
        else
            _ = embeddedContent.SyncAudioFrame(frame, isPlaying, position);

        if (IsAlbumWorldActive)
            _ = SyncAlbumWorldFrameAsync(frame, isPlaying, position);
    }

    private void UpdateUiState()
    {
        SyncNativeExternalPlaybackState();
        var track = GetActiveTrackForUi();
        var isPlaying = IsSpotifyActive ? spotifyIsPlaying : IsSoundCloudActive ? soundCloudIsPlaying : IsSunoActive ? sunoIsPlaying : IsYouTubeActive ? youTubeIsPlaying : engine.IsPlaying;
        EnsureEffectiveTheme();
        if (!IsSpotifyActive && !IsSoundCloudActive && !IsSunoActive && !IsYouTubeActive)
            UpdateAlbumArt(track);

        if (btnYouTubeVideo is not null)
        {
            btnYouTubeVideo.Visible = IsYouTubeActive;
            btnYouTubeVideo.Text = youTubeVideoMode ? "Audio" : "Video";
        }

        btnPlayPause.Text = GetPlayPauseButtonText(track, isPlaying);
        btnPlayPause.AccentColor = track is null && !IsSpotifyReady ? AccentSecondaryColor : AccentPrimaryColor;
        btnPlayPause.ForeColor = AccentContrastColor;

        btnStop.Enabled = track is not null;
        trackBarSeek.Enabled = track is not null;

        var externalStreamActive = IsSoundCloudActive || IsSunoActive || IsYouTubeActive;
        btnPrevious.Enabled = IsSpotifyActive || (!externalStreamActive && queue.Count > 1 && (queue.HasPrevious || (engine.IsLoaded && engine.GetPosition() > 3f)));
        btnNext.Enabled = IsSpotifyActive || (!externalStreamActive && (queue.HasNext || resumeSpotifyAfterLocalPlayback));
        btnToggleQueue.AccentColor = isQueueVisible ? AccentPrimaryColor : AccentSoftColor;
        btnToggleQueue.ForeColor = isQueueVisible ? AccentContrastColor : TextMutedColor;

        playbackPreviousToolStripMenuItem.Enabled = btnPrevious.Enabled;
        playbackNextToolStripMenuItem.Enabled = btnNext.Enabled;

        UpdateQueuePanel();

        lblNowPlaying.Text = track?.DisplayName ?? "Drop a file here or press Play";
        lblTrackInfo.Text = BuildTrackInfoText(track);
        ApplyInformationVisibility();
        var hasAppLyrics = IsAppLyricsAvailable(track);
        SetLyricsVisible(hasAppLyrics);
        btnInspectLyrics.Visible = hasAppLyrics;
        btnInspectLyrics.Enabled = hasAppLyrics && track?.Lyrics?.HasLines == true;
        var annotationCount = track?.Lyrics?.Lines.Count(static line => !string.IsNullOrWhiteSpace(line.Explanation)) ?? 0;
        btnInspectLyrics.Text = "Inspect Lyrics";
        btnInspectLyrics.IsGhost = annotationCount == 0;
        btnInspectLyrics.AccentColor = annotationCount > 0 ? AccentPrimaryColor : AccentSoftColor;
        btnInspectLyrics.ForeColor = annotationCount > 0 ? AccentContrastColor : TextSecondaryColor;
        var lyricPosition = IsSpotifyActive ? SpotifyCurrentPositionSeconds
            : IsSoundCloudActive ? soundCloudPositionSeconds
            : IsSunoActive ? sunoPositionSeconds
            : IsYouTubeActive ? youTubePositionSeconds
            : engine.GetPosition();
        lyricsView.UpdateState(hasAppLyrics ? track : null, lyricPosition);

        if (isMuted || trackBarVolume.Value == 0)
        {
            lblVolumeValue.Text = "Muted";
            lblVolumeValue.ForeColor = TextMutedColor;
            btnMute.Text = "Unmute";
            btnMute.AccentColor = DangerColor;
            btnMute.ForeColor = DangerTextColor;
        }
        else
        {
            lblVolumeValue.Text = $"{trackBarVolume.Value}%";
            lblVolumeValue.ForeColor = TextSecondaryColor;
            btnMute.Text = "Mute";
            btnMute.AccentColor = AccentSoftColor;
            btnMute.ForeColor = TextSecondaryColor;
        }

        if (!_sharedPlayLinkPromptActive)
        {
            var currentPos = IsYouTubeActive ? youTubePositionSeconds
                : IsSoundCloudActive ? soundCloudPositionSeconds
                : IsSunoActive ? sunoPositionSeconds
                : IsSpotifyActive ? spotifyPositionSeconds
                : engine.GetPosition();
            var baseStatus = GetJoinedSharedPlayStatusText() ??
                (!string.IsNullOrWhiteSpace(youTubeStatusMessage)
                    ? youTubeStatusMessage
                    : !string.IsNullOrWhiteSpace(soundCloudStatusMessage)
                        ? soundCloudStatusMessage
                    : !string.IsNullOrWhiteSpace(sunoStatusMessage)
                        ? sunoStatusMessage
                    : !string.IsNullOrWhiteSpace(spotifyStatusMessage)
                        ? spotifyStatusMessage
                    : track is null
                        ? IsSpotifyReady ? "Spotify ready" : "Ready"
                    : isPlaying ? "Playing"
                    : Math.Abs(currentPos) < 0.01f ? "Loaded" : "Paused");

            if (!IsSpotifyActive && queue.Count > 1 && track is not null)
                baseStatus += $"  ·  {queue.CurrentIndex + 1} of {queue.Count}";

            toolStripStatusLabel.Text = baseStatus;
            toolStripStatusLabel.ForeColor = HasJoinedSharedPlayActivity || isPlaying
                ? AccentPrimaryColor
                : TextMutedColor;
            toolStripStatusLabel.IsLink = false;
        }

        toolStripOutputLabel.Text = IsSpotifyActive
            ? "Output  Spotify"
            : IsSoundCloudActive
                ? "Output  SoundCloud"
            : IsSunoActive
                ? "Output  Suno"
            : IsYouTubeActive
                ? "Output  YouTube"
            : engine.IsLoaded
                ? $"Output  {engine.EffectiveSampleRate / 1000d:0.#} kHz"
            : IsSpotifyReady
                ? "Output  Spotify ready"
                : $"Output  {GetSelectedSampleRateLabel()}";
        toolStripHintLabel.ForeColor = TextMutedColor;
        UpdateMenuState();
        UpdateSharedPlayMenuState();
        toolStripOutputLabel.ForeColor = (IsSpotifyActive || IsSoundCloudActive || IsSunoActive || IsYouTubeActive || IsSpotifyReady || engine.IsLoaded) ? TextSecondaryColor : TextMutedColor;
        Text = track is null ? "Spectralis" : BuildWindowTitle(track);
        SyncNowPlayingState();
        SyncDiscordRichPresenceState();
    }

    private void UpdateMenuState()
    {
        playbackPlayPauseToolStripMenuItem.Text = GetPlayPauseButtonText(
            IsSpotifyActive ? spotifyCurrentTrack : IsSoundCloudActive ? soundCloudCurrentTrack : IsSunoActive ? sunoCurrentTrack : IsYouTubeActive ? youTubeCurrentTrack : engine.CurrentTrack,
            IsSpotifyActive ? spotifyIsPlaying : IsSoundCloudActive ? soundCloudIsPlaying : IsSunoActive ? sunoIsPlaying : IsYouTubeActive ? youTubeIsPlaying : engine.IsPlaying);
        playbackStopToolStripMenuItem.Enabled = IsSpotifyActive || IsSoundCloudActive || IsSunoActive || IsYouTubeActive || engine.IsLoaded;
        playbackMuteToolStripMenuItem.Text = isMuted || trackBarVolume.Value == 0 ? "Unmute" : "Mute";
        playbackMuteToolStripMenuItem.Enabled = IsYouTubeActive || IsSunoActive || engine.IsLoaded || trackBarVolume.Value > 0 || isMuted;
        helpClearRedeemedVisualizersToolStripMenuItem.Enabled = redeemableVisualizers.Installed.Count > 0;
    }

    private string GetPlayPauseButtonText(AudioTrackInfo? track, bool isPlaying)
    {
        if (track is not null)
            return isPlaying ? "Pause" : "Play";

        if (IsSpotifyReady)
            return "Play Spotify";

        if (spotifyService.IsLinked && !engine.IsLoaded)
            return "Start Spotify";

        return "Open Audio";
    }

    private void ApplyInformationVisibility() => lblTrackInfo.Visible = appSettings.ShowMoreInfo;

    private string GetSelectedSampleRateLabel() =>
        cmbSampleRate.SelectedItem is SelectionOption<int> option ? option.Label : "Match source";

    private static string FormatTime(float seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
    }

    private static string FormatBitDepth(int bitsPerSample) =>
        bitsPerSample > 0 ? $"{bitsPerSample}-bit" : "float";

    private static string BuildTrackInfoText(AudioTrackInfo? track)
    {
        const string separator = "  \u00B7  ";

        if (track is null)
        {
            return "Supports MP3, WAV, FLAC, AAC, M4A, MIDI, WMA, OGG Vorbis, AIFF, Opus, WebM, 3GP and more through installed Windows codecs.";
        }

        var descriptiveParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(track.Artist))
            descriptiveParts.Add(track.Artist);

        if (!string.IsNullOrWhiteSpace(track.Album))
            descriptiveParts.Add(track.Album);

        var technicalLine =
            $"{track.FormatName}{separator}{track.Channels} ch{separator}{track.SourceSampleRate / 1000d:0.#} kHz{separator}{FormatBitDepth(track.BitsPerSample)}{separator}{FormatTime((float)track.Duration.TotalSeconds)}";

        if (track.Lyrics is not null && track.SuppressAppLyrics is false)
        {
            technicalLine += track.Lyrics.IsDescription
                ? $"{separator}Lyrics"
                : track.Lyrics.HasWordTimings
                    ? $"{separator}Enhanced lyrics"
                    : $"{separator}Synced lyrics";
        }
        else if (track.Lyrics is not null && track.SuppressAppLyrics)
        {
            technicalLine += $"{separator}Visualizer lyrics";
        }

        if (track.EmbeddedVisualizer is not null)
        {
            technicalLine += $"{separator}Embedded visualizer";
        }

        if (track.EmbeddedTheme is not null)
        {
            technicalLine += $"{separator}Embedded theme";
        }

        if (track.EmbeddedHtml is not null ||
            track.EmbeddedMarkdown is not null ||
            track.EmbeddedVideo is not null)
        {
            technicalLine += $"{separator}Embedded content";
        }

        return descriptiveParts.Count == 0
            ? technicalLine
            : $"{string.Join(separator, descriptiveParts)}{Environment.NewLine}{technicalLine}";
    }

    private static string BuildWindowTitle(AudioTrackInfo track) =>
        string.IsNullOrWhiteSpace(track.Artist)
            ? $"{track.DisplayName}  \u2014  Spectralis"
            : $"{track.Artist} - {track.DisplayName}  \u2014  Spectralis";

    private void SetLyricsVisible(bool visible)
    {
        // Column layout is now managed by RefreshContentColumns (queue-aware).
        // This method is kept for compatibility with callers but defers to the queue-aware version.
        RefreshContentColumns(visible);
    }

    private AudioTrackInfo? GetActiveTrackForUi() =>
        IsSpotifyActive ? spotifyCurrentTrack
        : IsSoundCloudActive ? soundCloudCurrentTrack
        : IsSunoActive ? sunoCurrentTrack
        : IsYouTubeActive ? youTubeCurrentTrack
        : engine.CurrentTrack;

    private static bool IsAppLyricsAvailable(AudioTrackInfo? track) =>
        track?.Lyrics is not null && track.SuppressAppLyrics is false;

    private void ShowError(string message, string title)
    {
        MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void ShowAudioDeviceError()
    {
        var message = "The audio output device was disconnected or became unavailable. " +
                      "Please reconnect your audio device and try again.";
        ShowError(message, "Audio Device Error");
    }

    private void SetLoadingStatus(string message)
    {
        toolStripStatusLabel.Text = message;
        toolStripStatusLabel.ForeColor = TextSecondaryColor;
        statusStrip1.Refresh();
    }
}
