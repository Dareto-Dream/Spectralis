namespace Spectralis;

public partial class Form1
{
    private ScrobblingService? scrobblingService;
    private int _scrobbleTickCount;

    private void InitializeScrobbling()
    {
        scrobblingService = new ScrobblingService(appSettings);

        // ── File menu items ──────────────────────────────────────────────────
        var mniScrobbling = new ToolStripMenuItem
        {
            Name = "mniScrobbling",
            Text = "Scrobbling...",
        };
        mniScrobbling.Click += (_, _) => OpenScrobblingSettings();

        var mniStats = new ToolStripMenuItem
        {
            Name = "mniListeningStats",
            Text = "My Listening...",
        };
        mniStats.Click += (_, _) => OpenStatsDialog();

        libraryToolStripMenuItem.DropDownItems.Add(mniScrobbling);
        libraryToolStripMenuItem.DropDownItems.Add(mniStats);
        libraryToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());

        // Drain any offline queue from previous session
        _ = scrobblingService.DrainQueueAsync();
    }

    // ── Called every timer1_Tick (~50 ms); only acts every ~5 s ──────────────

    private void TickScrobbling()
    {
        if (scrobblingService is null) return;
        if (!appSettings.LastFmEnabled && !appSettings.ListenBrainzEnabled) return;

        _scrobbleTickCount++;
        if (_scrobbleTickCount < 100) return;  // ~5 s at 50 ms timer
        _scrobbleTickCount = 0;

        if (!engine.IsLoaded) return;
        scrobblingService.Tick(engine.GetPosition(), engine.IsPlaying);
    }

    // ── Partial hook: called by Form1.Playback when a local file loads ────────

    partial void OnScrobblingTrackLoaded(string path)
    {
        if (scrobblingService is null) return;

        var track = engine.CurrentTrack;
        if (track is null) return;

        scrobblingService.NotifyTrackLoaded(
            path,
            track.DisplayName,
            track.Artist ?? "",
            track.Album  ?? "",
            track.Duration.TotalSeconds);
    }

    // ── Menu handlers ─────────────────────────────────────────────────────────

    private void OpenScrobblingSettings()
    {
        using var dlg = new ScrobblingSettingsDialog(appSettings, themePalette);
        if (dlg.ShowDialog(this) != System.Windows.Forms.DialogResult.OK) return;

        var result = dlg.Result;
        appSettings.LastFmEnabled        = result.LastFmEnabled;
        appSettings.LastFmApiKey         = result.LastFmApiKey;
        appSettings.LastFmApiSecret      = result.LastFmApiSecret;
        appSettings.LastFmSessionKey     = result.LastFmSessionKey;
        appSettings.LastFmUsername       = result.LastFmUsername;
        appSettings.ListenBrainzEnabled  = result.ListenBrainzEnabled;
        appSettings.ListenBrainzToken    = result.ListenBrainzToken;
        appSettings.ListenBrainzUsername = result.ListenBrainzUsername;
        SaveAppSettings();

        // Recreate service with updated settings so it picks up new credentials
        scrobblingService = new ScrobblingService(appSettings);
        _ = scrobblingService.DrainQueueAsync();
    }

    private void OpenStatsDialog()
    {
        using var dlg = new StatsDialog(themePalette);
        dlg.ShowDialog(this);
    }
}
