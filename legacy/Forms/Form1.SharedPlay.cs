namespace Spectralis;

public partial class Form1
{
    private ToolStripSeparator? playbackSharedPlaySeparator;
    private ToolStripMenuItem? playbackCopySharedPlayLinkToolStripMenuItem;
    private ToolStripMenuItem? playbackCopyDiscordListenLinkToolStripMenuItem;
    private ToolStripMenuItem? playbackEnableLiveChannelToolStripMenuItem;
    private ToolStripMenuItem? playbackCopyLiveChannelLinkToolStripMenuItem;
    private string? _sharedPlayLastKnownJoinUrl;
    private bool _sharedPlayLinkPromptActive;

    private void InitializeSharedPlay()
    {
        playbackSharedPlaySeparator = new ToolStripSeparator();
        playbackCopySharedPlayLinkToolStripMenuItem = new ToolStripMenuItem
        {
            Name = "playbackCopySharedPlayLinkToolStripMenuItem",
            Text = "Copy Shared Play Link",
            Enabled = false
        };
        playbackCopySharedPlayLinkToolStripMenuItem.Click += playbackCopySharedPlayLinkToolStripMenuItem_Click;
        playbackCopyDiscordListenLinkToolStripMenuItem = new ToolStripMenuItem
        {
            Name = "playbackCopyDiscordListenLinkToolStripMenuItem",
            Text = "Copy Discord Listen Link",
            Enabled = false
        };
        playbackCopyDiscordListenLinkToolStripMenuItem.Click += playbackCopyDiscordListenLinkToolStripMenuItem_Click;
        playbackEnableLiveChannelToolStripMenuItem = new ToolStripMenuItem
        {
            Name = "playbackEnableLiveChannelToolStripMenuItem",
            Text = "Live Channel: Off",
            CheckOnClick = true
        };
        playbackEnableLiveChannelToolStripMenuItem.Click += playbackEnableLiveChannelToolStripMenuItem_Click;
        playbackCopyLiveChannelLinkToolStripMenuItem = new ToolStripMenuItem
        {
            Name = "playbackCopyLiveChannelLinkToolStripMenuItem",
            Text = "Copy Live Channel Link",
            Enabled = false
        };
        playbackCopyLiveChannelLinkToolStripMenuItem.Click += playbackCopyLiveChannelLinkToolStripMenuItem_Click;

        playbackToolStripMenuItem.DropDownItems.Add(playbackSharedPlaySeparator);
        playbackToolStripMenuItem.DropDownItems.Add(playbackCopySharedPlayLinkToolStripMenuItem);
        playbackToolStripMenuItem.DropDownItems.Add(playbackCopyDiscordListenLinkToolStripMenuItem);
        playbackToolStripMenuItem.DropDownItems.Add(playbackEnableLiveChannelToolStripMenuItem);
        playbackToolStripMenuItem.DropDownItems.Add(playbackCopyLiveChannelLinkToolStripMenuItem);

        sharedPlay.StatusChanged += sharedPlay_StatusChanged;
        toolStripStatusLabel.Click += toolStripStatusLabel_SharedPlayLinkClick;
        ApplySharedPlaySettings();
    }

    private void ApplySharedPlaySettings()
    {
        sharedPlay.ApplySettings(appSettings);
        UpdateSharedPlayMenuState();

        if (appSettings.EnableSharedPlay && engine.IsPlaying)
            NotifySharedPlayPlaybackChanged("settings-enabled");
    }

    private void NotifySharedPlayPlaybackChanged(string reason)
    {
        if (HasJoinedSharedPlayActivity || isApplyingJoinedSharedPlaySync)
            return;

        sharedPlay.NotifyPlaybackChanged(
            engine.CurrentTrack,
            engine.IsPlaying,
            engine.GetPosition(),
            engine.GetLength(),
            reason,
            BuildSharedPlayQueueSnapshot());
        foreach (var path in GetUpcomingSharedPlayLocalQueuePaths(2))
            sharedPlay.PrepareUpcomingTrack(path);
    }

    private void PulseSharedPlay()
    {
        if (HasJoinedSharedPlayActivity || !appSettings.EnableSharedPlay || !engine.IsPlaying)
            return;

        var now = Environment.TickCount64;
        if (now < nextSharedPlayPulseTick)
            return;

        nextSharedPlayPulseTick = now + 2000;
        NotifySharedPlayPlaybackChanged("tick");
        _ = PullSharedQueueAdditionsAsync();
    }

    private void UpdateSharedPlayMenuState()
    {
        if (playbackCopySharedPlayLinkToolStripMenuItem is null ||
            playbackCopyDiscordListenLinkToolStripMenuItem is null ||
            playbackEnableLiveChannelToolStripMenuItem is null ||
            playbackCopyLiveChannelLinkToolStripMenuItem is null ||
            playbackSharedPlaySeparator is null)
        {
            return;
        }

        var snapshot = sharedPlay.Snapshot;
        playbackSharedPlaySeparator.Visible = snapshot.IsEnabled;
        playbackCopySharedPlayLinkToolStripMenuItem.Visible = snapshot.IsEnabled;
        playbackCopyDiscordListenLinkToolStripMenuItem.Visible = snapshot.IsEnabled;
        playbackEnableLiveChannelToolStripMenuItem.Visible = snapshot.IsEnabled;
        playbackCopyLiveChannelLinkToolStripMenuItem.Visible = snapshot.IsEnabled;
        playbackEnableLiveChannelToolStripMenuItem.Checked = appSettings.EnableSharedPlayLiveChannel;
        playbackEnableLiveChannelToolStripMenuItem.Text = appSettings.EnableSharedPlayLiveChannel
            ? "Live Channel: On"
            : "Live Channel: Off";
        playbackCopyLiveChannelLinkToolStripMenuItem.Enabled = appSettings.EnableSharedPlayLiveChannel &&
            !string.IsNullOrWhiteSpace(snapshot.ChannelUrl);

        if (HasJoinedSharedPlayActivity)
        {
            playbackSharedPlaySeparator.Visible = true;
            playbackCopySharedPlayLinkToolStripMenuItem.Visible = true;
            playbackCopyDiscordListenLinkToolStripMenuItem.Visible = false;
            playbackCopySharedPlayLinkToolStripMenuItem.Enabled = true;
            playbackCopySharedPlayLinkToolStripMenuItem.Text = isJoiningSharedPlay
                ? "Cancel Shared Play Join"
                : "Leave Shared Play";
            return;
        }

        if (!snapshot.IsEnabled)
        {
            playbackCopySharedPlayLinkToolStripMenuItem.Enabled = false;
            playbackCopyDiscordListenLinkToolStripMenuItem.Enabled = false;
            playbackEnableLiveChannelToolStripMenuItem.Enabled = false;
            playbackCopyLiveChannelLinkToolStripMenuItem.Enabled = false;
            return;
        }

        playbackEnableLiveChannelToolStripMenuItem.Enabled = true;
        playbackCopySharedPlayLinkToolStripMenuItem.Enabled = !string.IsNullOrWhiteSpace(snapshot.JoinUrl);
        playbackCopyDiscordListenLinkToolStripMenuItem.Enabled = !string.IsNullOrWhiteSpace(snapshot.JoinUrl);
        playbackCopySharedPlayLinkToolStripMenuItem.Text = snapshot switch
        {
            { IsUploading: true } => "Shared Play Uploading...",
            { JoinUrl: not null } => "Copy Shared Play Link",
            { LastError: not null } => "Shared Play Unavailable - View Error",
            _ => "Shared Play Waiting"
        };
        playbackCopySharedPlayLinkToolStripMenuItem.ToolTipText = snapshot.LastError ?? "";
        playbackCopyDiscordListenLinkToolStripMenuItem.Text = snapshot.JoinUrl is null
            ? snapshot.LastError is null ? "Discord Listen Link Waiting" : "Discord Listen Link Unavailable"
            : "Copy Discord Listen Link";
        playbackCopyDiscordListenLinkToolStripMenuItem.ToolTipText = snapshot.LastError ?? "";
    }

    private void sharedPlay_StatusChanged(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(UpdateSharedPlayStatusState));
            return;
        }

        UpdateSharedPlayStatusState();
    }

    private void UpdateSharedPlayStatusState()
    {
        var snapshot = sharedPlay.Snapshot;
        var previousUrl = _sharedPlayLastKnownJoinUrl;
        _sharedPlayLastKnownJoinUrl = snapshot.JoinUrl;

        if (_sharedPlayLinkPromptActive && snapshot.JoinUrl is null)
        {
            _sharedPlayLinkPromptActive = false;
            toolStripStatusLabel.IsLink = false;
        }

        UpdateSharedPlayMenuState();
        SyncDiscordRichPresenceState();

        if (previousUrl is null &&
            snapshot.JoinUrl is not null &&
            !HasJoinedSharedPlayActivity)
        {
            toolStripStatusLabel.Text = "Shared Play link ready — click to copy";
            toolStripStatusLabel.ForeColor = AccentPrimaryColor;
            toolStripStatusLabel.IsLink = true;
            _sharedPlayLinkPromptActive = true;
        }
        else if (previousUrl is null &&
            snapshot.JoinUrl is null &&
            snapshot.LastError is not null &&
            !_sharedPlayLinkPromptActive &&
            !HasJoinedSharedPlayActivity)
        {
            toolStripStatusLabel.Text = $"Shared Play unavailable - {snapshot.LastError}";
            toolStripStatusLabel.ForeColor = TextMutedColor;
        }
    }

    private void toolStripStatusLabel_SharedPlayLinkClick(object? sender, EventArgs e)
    {
        if (!toolStripStatusLabel.IsLink)
            return;

        var url = sharedPlay.Snapshot.JoinUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;

        Clipboard.SetText(url);
        toolStripStatusLabel.Text = "Shared Play link copied";
        toolStripStatusLabel.IsLink = false;
        _sharedPlayLinkPromptActive = false;
    }

    private void playbackCopySharedPlayLinkToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        if (HasJoinedSharedPlayActivity)
        {
            StopJoinedSharedPlaySession(stopPlayback: true, clearStatus: true);
            toolStripStatusLabel.Text = "Left Shared Play";
            toolStripStatusLabel.ForeColor = TextMutedColor;
            UpdateUiState();
            return;
        }

        var snapshot = sharedPlay.Snapshot;
        if (string.IsNullOrWhiteSpace(snapshot.JoinUrl))
        {
            var message = string.IsNullOrWhiteSpace(snapshot.LastError)
                ? "Start playback with Shared Play enabled to create a private join link."
                : $"Shared Play could not create a join link yet.{Environment.NewLine}{Environment.NewLine}{snapshot.LastError}";

            MessageBox.Show(this, message, "Shared Play", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Clipboard.SetText(snapshot.JoinUrl);
        toolStripStatusLabel.Text = "Shared Play link copied";
        toolStripStatusLabel.ForeColor = AccentPrimaryColor;
    }

    private void playbackCopyDiscordListenLinkToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        var url = GetDiscordListenTogetherUrl();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(
                this,
                "Start playback with Shared Play enabled to create a Discord listen link.",
                "Discord Listen Together",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var track = engine.CurrentTrack;
        var title = track is null ? "this track" : track.DisplayName;
        Clipboard.SetText($"Listen with me in Spectralis: {title}{Environment.NewLine}{url}");
        toolStripStatusLabel.Text = "Discord listen link copied";
        toolStripStatusLabel.ForeColor = AccentPrimaryColor;
    }

    private void playbackEnableLiveChannelToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        appSettings.EnableSharedPlayLiveChannel = playbackEnableLiveChannelToolStripMenuItem?.Checked ?? !appSettings.EnableSharedPlayLiveChannel;
        AppSettingsStore.Save(appSettings);
        ApplySharedPlaySettings();
        NotifySharedPlayPlaybackChanged(appSettings.EnableSharedPlayLiveChannel ? "live-channel-on" : "live-channel-off");
        toolStripStatusLabel.Text = appSettings.EnableSharedPlayLiveChannel
            ? "Live Channel enabled"
            : "Live Channel disabled";
        toolStripStatusLabel.ForeColor = TextMutedColor;
    }

    private void playbackCopyLiveChannelLinkToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        if (!appSettings.EnableSharedPlayLiveChannel)
        {
            appSettings.EnableSharedPlayLiveChannel = true;
            AppSettingsStore.Save(appSettings);
            ApplySharedPlaySettings();
            NotifySharedPlayPlaybackChanged("live-channel-on");
        }

        var url = sharedPlay.Snapshot.ChannelUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(
                this,
                "Enable Shared Play to create a permanent Live Channel link.",
                "Live Channel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Clipboard.SetText(url);
        toolStripStatusLabel.Text = "Live Channel link copied";
        toolStripStatusLabel.ForeColor = AccentPrimaryColor;
    }
}
