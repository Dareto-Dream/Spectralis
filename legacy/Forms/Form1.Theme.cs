using System.Drawing;

namespace Spectralis;

public partial class Form1
{
    private void ApplyTheme()
    {
        var (themeMode, themeAccent) = GetEffectiveThemeSelection(engine.CurrentTrack);
        themePalette = ThemePalette.Create(themeMode, themeAccent);
        appliedThemeMode = themeMode;
        appliedThemeAccent = themeAccent;
        hasAppliedTheme = true;

        WindowChromeStyler.ApplyTheme(this, themePalette);

        BackColor = WindowBackColor;
        ForeColor = TextPrimaryColor;
        ApplyAppIcon();

        statusStrip1.BackColor = WindowBackColor;
        statusStrip1.ForeColor = TextSecondaryColor;
        statusStrip1.Renderer = new ThemeStatusStripRenderer(WindowBackColor, StatusBorderColor);
        menuStrip1.BackColor = WindowBackColor;
        menuStrip1.ForeColor = TextPrimaryColor;
        menuStrip1.Renderer = new ThemeMenuStripRenderer(WindowBackColor, SurfaceAltBackColor, SurfaceBackColor, StatusBorderColor);
        ctxQueue.Renderer = new ThemeMenuStripRenderer(WindowBackColor, SurfaceAltBackColor, SurfaceBackColor, StatusBorderColor);

        toolStripOutputLabel.ForeColor = TextSecondaryColor;
        toolStripHintLabel.ForeColor = TextMutedColor;
        toolStripBrandLabel.ForeColor = TextSecondaryColor;
        toolStripBrandLabel.LinkColor = AccentPrimaryColor;
        toolStripBrandLabel.ActiveLinkColor = AccentSecondaryColor;
        toolStripBrandLabel.VisitedLinkColor = AccentPrimaryColor;

        picAlbumArt.BackColor = SurfaceBackColor;
        lblNowPlaying.ForeColor = TextPrimaryColor;
        lblTrackInfo.ForeColor = TextSecondaryColor;
        lblCurrentTime.ForeColor = TextSoftColor;
        lblDuration.ForeColor = TextMutedColor;
        lblVolumeValue.ForeColor = TextSecondaryColor;

        lblVisualizerModeCaption.ForeColor = TextMutedColor;
        lblPeakHoldCaption.ForeColor = TextMutedColor;
        lblVisualizerAutoCycleCaption.ForeColor = TextMutedColor;
        lblSampleRateCaption.ForeColor = TextMutedColor;
        lblSensitivityCaption.ForeColor = TextMutedColor;
        ThemeControlStyler.ApplySwitchTheme(chkPeakHold, themePalette);

        // Visualizer nav panel
        ThemeControlStyler.ApplySwitchTheme(chkVisualizerAutoCycle, themePalette);

        ThemeControlStyler.ApplyComboBoxTheme(cmbVisualizerMode, themePalette);
        ThemeControlStyler.ApplyComboBoxTheme(cmbSampleRate, themePalette);
        ThemeControlStyler.ApplySliderTheme(trackBarSeek, themePalette);
        ThemeControlStyler.ApplySliderTheme(trackBarVolume, themePalette);
        ThemeControlStyler.ApplySliderTheme(trackBarSensitivity, themePalette);
        ApplyMenuTheme(menuStrip1.Items);
        toolStripVersionLabel.ForeColor = TextMutedColor;
        visualizerControl.ApplyTheme(themePalette);
        lyricsView.ApplyTheme(themePalette);
        capsuleStoryControl?.ApplyTheme(themePalette);
        ApplyLibraryTheme();
        ApplyPlaylistsTheme();

        transportLayout.Margin = new Padding(0, 14, 0, 0);
        leftButtonsPanel.Padding = Padding.Empty;
        rightControlsPanel.Padding = Padding.Empty;
        settingsPanel.Padding = Padding.Empty;
        settingsPanel.Margin = Padding.Empty;
        settingsPanel.Visible = false;

        ThemeControlStyler.ApplyPrimaryButtonTheme(btnPlayPause, themePalette, AccentPrimaryColor);
        btnPlayPause.Pill = false;
        btnPlayPause.Font = new Font("Segoe UI Semibold", 9.25F, FontStyle.Bold, GraphicsUnit.Point);
        btnPlayPause.Size = new Size(158, 40);

        ThemeControlStyler.ApplyGhostButtonTheme(btnStop, themePalette, DangerColor);
        btnStop.Font = new Font("Segoe UI Semibold", 8.75F, FontStyle.Bold, GraphicsUnit.Point);
        btnStop.Margin = new Padding(0, 0, 10, 0);
        btnStop.Size = new Size(74, 38);

        ThemeControlStyler.ApplyGhostButtonTheme(btnMute, themePalette, AccentSoftColor);
        btnMute.Font = new Font("Segoe UI Semibold", 8.75F, FontStyle.Bold, GraphicsUnit.Point);
        btnMute.Margin = new Padding(0, 0, 12, 0);
        btnMute.Size = new Size(74, 38);

        ThemeControlStyler.ApplyGhostButtonTheme(btnDefaultApp, themePalette, AccentSoftColor);
        btnDefaultApp.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        btnDefaultApp.Margin = new Padding(18, 0, 0, 0);
        btnDefaultApp.Size = new Size(136, 32);

        trackBarVolume.Margin = new Padding(0, 0, 12, 0);
        trackBarVolume.Size = new Size(148, 38);
        trackBarSensitivity.Margin = new Padding(0, 0, 18, 0);
        trackBarSensitivity.Size = new Size(116, 34);

        // Queue panel
        pnlQueue.BackColor = SurfaceBackColor;
        pnlQueueHeader.BackColor = SurfaceBackColor;
        lblQueueHeader.ForeColor = TextSoftColor;
        lstQueue.BackColor = SurfaceBackColor;
        lstQueue.Theme = themePalette;
        lstQueue.Invalidate();
        ThemeControlStyler.ApplyGhostButtonTheme(btnPrevious, themePalette, AccentSoftColor);
        btnPrevious.Font = new Font("Segoe UI Semibold", 8.75F, FontStyle.Bold, GraphicsUnit.Point);
        btnPrevious.Size = new Size(54, 38);
        ThemeControlStyler.ApplyGhostButtonTheme(btnNext, themePalette, AccentSoftColor);
        btnNext.Font = new Font("Segoe UI Semibold", 8.75F, FontStyle.Bold, GraphicsUnit.Point);
        btnNext.Size = new Size(54, 38);
        ThemeControlStyler.ApplyGhostButtonTheme(btnToggleQueue, themePalette, AccentSoftColor);
        btnToggleQueue.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
        btnToggleQueue.Size = new Size(60, 38);
        ThemeControlStyler.ApplyGhostButtonTheme(btnVisualizerPrev, themePalette, AccentSoftColor);
        btnVisualizerPrev.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point);
        btnVisualizerPrev.Margin = new Padding(0, 0, 4, 0);
        btnVisualizerPrev.Size = new Size(32, 34);
        ThemeControlStyler.ApplyGhostButtonTheme(btnVisualizerNext, themePalette, AccentSoftColor);
        btnVisualizerNext.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point);
        btnVisualizerNext.Margin = new Padding(0, 0, 18, 0);
        btnVisualizerNext.Size = new Size(32, 34);
        ThemeControlStyler.ApplyPrimaryButtonTheme(btnInspectLyrics, themePalette, AccentPrimaryColor);
        btnInspectLyrics.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point);
        btnInspectLyrics.Margin = new Padding(14, 0, 0, 0);
        btnInspectLyrics.Size = new Size(124, 34);
        ThemeControlStyler.ApplyGhostButtonTheme(btnQueueShuffle, themePalette, AccentSoftColor);
        btnQueueShuffle.Font = new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point);
        btnQueueShuffle.Size = new Size(52, 24);
        ThemeControlStyler.ApplyGhostButtonTheme(btnQueueRepeat, themePalette, AccentSoftColor);
        btnQueueRepeat.Font = new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point);
        btnQueueRepeat.Size = new Size(72, 24);
        ThemeControlStyler.ApplyGhostButtonTheme(btnQueueClear, themePalette, DangerColor);
        btnQueueClear.Font = new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point);
        btnQueueClear.Size = new Size(40, 24);
        UpdateQueueModeButtons();
        ApplyClipboardPopupTheme();

        ApplyInformationVisibility();
    }

    private void EnsureEffectiveTheme()
    {
        var (themeMode, themeAccent) = GetEffectiveThemeSelection(engine.CurrentTrack);
        if (hasAppliedTheme &&
            appliedThemeMode == themeMode &&
            appliedThemeAccent == themeAccent)
        {
            return;
        }

        ApplyTheme();
    }

    private (ThemeMode Mode, ThemeAccent Accent) GetEffectiveThemeSelection(AudioTrackInfo? track)
    {
        if (appSettings.UseEmbeddedTrackThemes &&
            track?.EmbeddedTheme is { } embeddedTheme)
        {
            return (embeddedTheme.Mode, embeddedTheme.Accent);
        }

        return (appSettings.ThemeMode, appSettings.ThemeAccent);
    }

    private void ApplyAppIcon()
    {
        var extractedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (extractedIcon is null)
            return;

        using (extractedIcon)
        {
            Icon = (Icon)extractedIcon.Clone();
        }
    }

    private void ApplyMenuTheme(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.ForeColor = TextPrimaryColor;

            if (item is ToolStripMenuItem menuItem)
                ApplyMenuTheme(menuItem.DropDownItems);
        }
    }
}
