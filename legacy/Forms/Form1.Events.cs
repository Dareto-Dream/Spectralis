namespace Spectralis;

public partial class Form1
{
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ActiveControl is ComboBox { DroppedDown: true })
            return base.ProcessCmdKey(ref msg, keyData);

        if (keyData == Keys.Oemcomma)
        {
            PreviousVisualizerMode();
            ResetVisualizerCycleDeadline();
            return true;
        }

        if (keyData == Keys.OemPeriod)
        {
            AdvanceVisualizerMode();
            ResetVisualizerCycleDeadline();
            return true;
        }

        if (ActiveControl is ComboBox)
            return base.ProcessCmdKey(ref msg, keyData);

        switch (keyData)
        {
            case Keys.Space:
                btnPlayPause_Click(this, EventArgs.Empty);
                return true;

            case Keys.Escape:
                ResetPlaybackSession();
                return true;

            case Keys.Left:
                SeekRelative(-5);
                return true;

            case Keys.Right:
                SeekRelative(5);
                return true;

            case Keys.Shift | Keys.Left:
                SeekRelative(-30);
                return true;

            case Keys.Shift | Keys.Right:
                SeekRelative(30);
                return true;

            case Keys.Control | Keys.Left:
                NavigatePrevious();
                return true;

            case Keys.Control | Keys.Right:
                NavigateNext();
                return true;

            case Keys.Up:
                AdjustVolume(5);
                return true;

            case Keys.Down:
                AdjustVolume(-5);
                return true;

            case Keys.M:
                btnMute_Click(this, EventArgs.Empty);
                return true;

            case Keys.Control | Keys.O:
                OpenAudioFile(startPlayback: true);
                return true;

            case Keys.Control | Keys.Shift | Keys.O:
                AddFilesToQueue();
                return true;

            case Keys.Control | Keys.B:
                ShowLibraryView();
                return true;

            case Keys.Control | Keys.P:
                ShowPlaylistsView();
                return true;

            case Keys.Control | Keys.L:
                ShowOpenUrlDialog();
                return true;

            case Keys.Control | Keys.Shift | Keys.L:
                ShowLyricsTimingStudio();
                return true;

            case Keys.Control | Keys.Q:
                ToggleQueuePanel();
                return true;

            case Keys.Control | Keys.Oemcomma:
                ShowSettingsDialog();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void cmbVisualizerMode_SelectedIndexChanged(object sender, EventArgs e)
    {
        ApplyVisualizerSettings();
    }

    private void cmbSampleRate_SelectedIndexChanged(object sender, EventArgs e)
    {
        if (isApplyingSettings || cmbSampleRate.SelectedItem is not SelectionOption<int> option)
            return;

        appSettings.PreferredSampleRate = option.Value;
        engine.SetPreferredSampleRate(option.Value);
        SaveAppSettings();
        UpdateUiState();
    }

    private void chkPeakHold_CheckedChanged(object sender, EventArgs e)
    {
        if (!isApplyingSettings)
        {
            appSettings.PeakHold = chkPeakHold.Checked;
            SaveAppSettings();
        }

        ApplyVisualizerSettings();
    }

    private void trackBarSensitivity_Scroll(object sender, EventArgs e)
    {
        if (!isApplyingSettings)
        {
            appSettings.VisualizerSensitivity = trackBarSensitivity.Value;
            SaveAppSettings();
        }

        ApplyVisualizerSettings();
    }

    private void trackBarVolume_Scroll(object sender, EventArgs e)
    {
        if (isMuted && trackBarVolume.Value > 0)
            isMuted = false;

        engine.Volume = trackBarVolume.Value / 100f;
        if (IsYouTubeActive)
            _ = YouTubeSetVolumeAsync(trackBarVolume.Value / 100f);
        if (IsSunoActive)
            _ = SunoSetVolumeAsync(trackBarVolume.Value / 100f);
        UpdateUiState();
    }

    private void trackBarSeek_Scroll(object sender, EventArgs e)
    {
        if (isUpdatingSeekBar)
            return;

        if (IsYouTubeActive)
        {
            _ = YouTubeSeekAsync(trackBarSeek.Value);
            return;
        }

        if (IsSoundCloudActive)
        {
            _ = SoundCloudSeekAsync(trackBarSeek.Value);
            return;
        }

        if (IsSunoActive)
        {
            _ = SunoSeekAsync(trackBarSeek.Value);
            return;
        }

        if (IsSpotifyActive)
        {
            _ = SpotifySeekAsync(trackBarSeek.Value);
            return;
        }

        engine.Seek(trackBarSeek.Value);
        SeekReactive(trackBarSeek.Value);
        NotifySharedPlayPlaybackChanged("seek");
        UpdateUiState();
    }

    private void lblCurrentTime_Click(object sender, EventArgs e)
    {
        showRemainingTime = !showRemainingTime;
        UpdateSeekBar();
    }

    private void timer1_Tick(object sender, EventArgs e)
    {
        SyncNativeExternalPlaybackState();
        UpdateSeekBar();
        UpdateVisualizer();
        CycleVisualizerIfDue();
        PulseJoinedSharedPlay();
        PulseSharedPlay();
        PulseObs();
        AdvanceReactive();
        TickAlbumWorld();
        TickScrobbling();
        TickKaraoke();
        if (!IsSpotifyActive && !IsSoundCloudActive && !IsSunoActive && !IsYouTubeActive)
            CheckAutoAdvance();
        UpdateUiState();
    }

    private void Form1_DragEnter(object? sender, DragEventArgs e)
    {
        var hasFiles = e.Data?.GetDataPresent(DataFormats.FileDrop) == true;
        e.Effect = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void Form1_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return;

        _ = HandleDroppedAudioFilesAsync(files);
    }

    private void WireFileDrop(Control control)
    {
        if (control is not (ButtonBase or ModernButton or ModernSlider or ModernSwitch or ComboBox or CheckBox or ListBox)
            && control != lstQueue)
        {
            control.AllowDrop = true;
            control.DragEnter -= Form1_DragEnter;
            control.DragDrop -= Form1_DragDrop;
            control.DragEnter += Form1_DragEnter;
            control.DragDrop += Form1_DragDrop;
        }

        foreach (Control child in control.Controls)
            WireFileDrop(child);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SaveWindowPlacement();
        timer1.Stop();
        DisposeDisplayedArtwork();
        visualizerAlbumArt?.Dispose();
        nowPlaying.CommandRequested -= NowPlaying_CommandRequested;
        nowPlaying.SeekRequested -= NowPlaying_SeekRequested;
        nowPlaying.Dispose();
        discordRichPresence.Dispose();
        DisposeSpotify();
        DisposeSoundCloud();
        DisposeYouTubeVideo();
        DisposeYouTube();
        DisposeSuno();
        DisposeClipboardMonitor();
        StopJoinedSharedPlaySession(stopPlayback: false, clearStatus: true);
        sharedPlayReceiverClient.Dispose();
        sharedPlay.Dispose();
        redeemableVisualizers.Dispose();
        DisposeObs();
        DisposeSongWars();
        DisposeCapsule();
        DisposeAlbumWorld();
        DisposeExternalOpenIpc();
        engine.Dispose();
        base.OnFormClosed(e);
    }

    private void fileOpenToolStripMenuItem_Click(object sender, EventArgs e) => OpenAudioFile(startPlayback: true);

    private void fileSettingsToolStripMenuItem_Click(object sender, EventArgs e) => ShowSettingsDialog();

    private void fileSetDefaultToolStripMenuItem_Click(object sender, EventArgs e) => btnDefaultApp_Click(sender, e);

    private void fileP2wModeToolStripMenuItem_Click(object sender, EventArgs e) => ToggleP2wMode();

    private void fileExitToolStripMenuItem_Click(object sender, EventArgs e) => Close();

    private void playbackPlayPauseToolStripMenuItem_Click(object sender, EventArgs e) => btnPlayPause_Click(sender, e);

    private void playbackStopToolStripMenuItem_Click(object sender, EventArgs e) => btnStop_Click(sender, e);

    private void playbackMuteToolStripMenuItem_Click(object sender, EventArgs e) => btnMute_Click(sender, e);

    private void helpAboutDeltavDevsToolStripMenuItem_Click(object sender, EventArgs e) => ShowAboutDeltavDevs();

    private void helpVisitDeltavDevsToolStripMenuItem_Click(object sender, EventArgs e) => OpenDeltavDevsSite();

    private void toolStripBrandLabel_Click(object sender, EventArgs e) => OpenDeltavDevsSite();

    private void btnVisualizerPrev_Click(object sender, EventArgs e)
    {
        PreviousVisualizerMode();
        ResetVisualizerCycleDeadline();
    }

    private void btnVisualizerNext_Click(object sender, EventArgs e)
    {
        AdvanceVisualizerMode();
        ResetVisualizerCycleDeadline();
    }

    private void chkVisualizerAutoCycle_CheckedChanged(object sender, EventArgs e)
    {
        if (isApplyingSettings)
            return;

        appSettings.EnableVisualizerAutoCycle = chkVisualizerAutoCycle.Checked;
        SaveAppSettings();
        ResetVisualizerCycleDeadline();
    }
}
