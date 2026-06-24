namespace Spectralis;

public partial class Form1
{
    private void RestoreWindowPlacement()
    {
        if (!appSettings.RememberWindowPlacement)
            return;

        var savedBounds = new Rectangle(
            appSettings.WindowX,
            appSettings.WindowY,
            appSettings.WindowWidth,
            appSettings.WindowHeight);

        if (!IsUsableWindowBounds(savedBounds))
            return;

        StartPosition = FormStartPosition.Manual;
        Bounds = savedBounds;
        if (appSettings.WindowMaximized)
            WindowState = FormWindowState.Maximized;
    }

    private void SaveWindowPlacement()
    {
        if (!appSettings.RememberWindowPlacement)
            return;

        var boundsToSave = WindowState == FormWindowState.Normal
            ? Bounds
            : RestoreBounds;

        if (!IsUsableWindowBounds(boundsToSave))
            return;

        appSettings.WindowX = boundsToSave.X;
        appSettings.WindowY = boundsToSave.Y;
        appSettings.WindowWidth = boundsToSave.Width;
        appSettings.WindowHeight = boundsToSave.Height;
        appSettings.WindowMaximized = WindowState == FormWindowState.Maximized;
        SaveAppSettings();
    }

    private bool IsUsableWindowBounds(Rectangle bounds)
    {
        if (bounds.Width < MinimumSize.Width || bounds.Height < MinimumSize.Height)
            return false;

        return Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds));
    }

    private void PopulateSettings()
    {
        ApplyStoredSettings(GetDefaultVisualizerChoice());
    }

    private void ApplyStoredSettings(VisualizerChoice currentVisualizer)
    {
        var normalizedSettings = AppSettingsStore.Normalize(appSettings.Clone());
        appSettings = normalizedSettings;
        visualizerControl.EmbeddedVisualizer = appSettings.UseEmbeddedTrackVisualizers
            ? engine.CurrentTrack?.EmbeddedVisualizer
            : null;

        isApplyingSettings = true;
        try
        {
            RefreshVisualizerModeOptions(currentVisualizer);

            var sampleRateOptions = GetSampleRateOptions();
            cmbSampleRate.BeginUpdate();
            try
            {
                cmbSampleRate.Items.Clear();
                cmbSampleRate.Items.AddRange(sampleRateOptions.Select(static option => (object)option).ToArray());
                SelectComboValue(cmbSampleRate, appSettings.PreferredSampleRate);
            }
            finally
            {
                cmbSampleRate.EndUpdate();
            }

            trackBarSensitivity.Value = appSettings.VisualizerSensitivity;
            trackBarVolume.Value = appSettings.DefaultVolume;
            chkPeakHold.Checked = appSettings.PeakHold;
            chkVisualizerAutoCycle.Checked = appSettings.EnableVisualizerAutoCycle;
            preMuteVolume = Math.Max(0.01f, trackBarVolume.Value / 100f);
            isMuted = trackBarVolume.Value == 0;
            engine.SetMidiPlaybackInstrument(appSettings.MidiInstrument);
            engine.SetPreferredSampleRate(appSettings.PreferredSampleRate);
            engine.Volume = trackBarVolume.Value / 100f;
        }
        finally
        {
            isApplyingSettings = false;
        }

        ApplyVisualizerSettings();
        UpdateEmbeddedContent(engine.CurrentTrack, force: true);
        ApplySharedPlaySettings();
        ApplyClipboardMonitorSettings();
        ApplyInformationVisibility();
        ResetVisualizerCycleDeadline();
    }

    private void ApplyVisualizerSettings()
    {
        var choice = (cmbVisualizerMode.SelectedItem as SelectionOption<VisualizerChoice>)?.Value
            ?? GetDefaultVisualizerChoice();

        if (visualizerControl.UsesEmbeddedVisualizer)
        {
            SetInstalledHtmlVisualizer(null, null);
            visualizerControl.InstalledVisualizer = null;
            visualizerControl.ScriptedRenderer = null;
        }
        else if (choice.TryGetInstalledId(out var installedId) &&
            redeemableVisualizers.TryGetInstalled(installedId, out var installedVisualizer))
        {
            visualizerControl.ScriptedRenderer = null;
            visualizerControl.Mode = choice.FallbackMode;
            if (installedVisualizer.Context is not null)
            {
                SetInstalledHtmlVisualizer(null, null);
                visualizerControl.InstalledVisualizer = installedVisualizer.Context;
            }
            else if (installedVisualizer.HtmlContext is not null)
            {
                visualizerControl.InstalledVisualizer = null;
                SetInstalledHtmlVisualizer(installedVisualizer.Id, installedVisualizer.HtmlContext);
            }
        }
        else if (choice.TryGetScriptedId(out var scriptedId) &&
            ScriptedVisualizerStore.TryGet(scriptedId, out var scriptedDef))
        {
            SetInstalledHtmlVisualizer(null, null);
            visualizerControl.InstalledVisualizer = null;
            visualizerControl.ScriptedRenderer = new ScriptVisualizerRenderer(scriptedDef);
            visualizerControl.Mode = VisualizerMode.MirrorSpectrum;
        }
        else
        {
            SetInstalledHtmlVisualizer(null, null);
            visualizerControl.InstalledVisualizer = null;
            visualizerControl.ScriptedRenderer = null;
            visualizerControl.Mode = choice.FallbackMode;
        }

        visualizerControl.ShowPeaks = chkPeakHold.Checked;
        visualizerControl.Sensitivity = trackBarSensitivity.Value / 100f;

        if (!isApplyingSettings)
            ResetVisualizerCycleDeadline();

        UpdateVisualizerNameLabel();
    }

    private void RefreshVisualizerModeOptions(VisualizerChoice? preferredChoice = null)
    {
        var selectedChoice = preferredChoice
            ?? (cmbVisualizerMode.SelectedItem as SelectionOption<VisualizerChoice>)?.Value
            ?? GetDefaultVisualizerChoice();
        var hasAlbumArt = visualizerAlbumArt is not null;
        var isMidi = engine.CurrentTrack?.IsMidi == true;
        selectedChoice = GetPreferredVisualizerChoice(selectedChoice, hasAlbumArt, isMidi);
        var isLockedToEmbedded = visualizerControl.UsesEmbeddedVisualizer;
        var availableModes = isLockedToEmbedded
            ? [CreateEmbeddedVisualizerOption(selectedChoice)]
            : GetVisualizerModeOptions(hasAlbumArt, isMidi);

        var wasApplyingSettings = isApplyingSettings;
        isApplyingSettings = true;
        cmbVisualizerMode.BeginUpdate();
        try
        {
            cmbVisualizerMode.Items.Clear();
            cmbVisualizerMode.Items.AddRange(availableModes.Select(static option => (object)option).ToArray());
            var selectedIndex = Array.FindIndex(
                availableModes,
                option => string.Equals(
                    option.Value.ToSettingsKey(),
                    selectedChoice.ToSettingsKey(),
                    StringComparison.OrdinalIgnoreCase));
            cmbVisualizerMode.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
            cmbVisualizerMode.Enabled = !isLockedToEmbedded && availableModes.Length > 1;
        }
        finally
        {
            cmbVisualizerMode.EndUpdate();
            isApplyingSettings = wasApplyingSettings;
        }

        ApplyVisualizerSettings();
        ResetVisualizerCycleDeadline();
        UpdateVisualizerNameLabel();
    }

    private void UpdateVisualizerNameLabel()
    {
        btnVisualizerPrev.Enabled = !visualizerControl.UsesEmbeddedVisualizer && cmbVisualizerMode.Items.Count > 1;
        btnVisualizerNext.Enabled = !visualizerControl.UsesEmbeddedVisualizer && cmbVisualizerMode.Items.Count > 1;
        chkVisualizerAutoCycle.Enabled = !visualizerControl.UsesEmbeddedVisualizer;
    }

    private void PreviousVisualizerMode()
    {
        if (visualizerControl.UsesEmbeddedVisualizer || cmbVisualizerMode.Items.Count <= 1)
            return;

        cmbVisualizerMode.SelectedIndex = cmbVisualizerMode.SelectedIndex switch
        {
            <= 0 => cmbVisualizerMode.Items.Count - 1,
            var i => i - 1
        };
    }

    private SelectionOption<VisualizerChoice>[] GetAvailableVisualizerModeOptions() =>
        GetVisualizerModeOptions(visualizerAlbumArt is not null, engine.CurrentTrack?.IsMidi == true);

    private SelectionOption<VisualizerChoice>[] GetAllVisualizerModeOptions() =>
        GetVisualizerModeOptions(includeAlbumArtDependent: true, isMidi: true);

    private static SelectionOption<int>[] GetSampleRateOptions() =>
    [
        new("Match source", 0),
        new("44.1 kHz", 44100),
        new("48 kHz", 48000),
        new("88.2 kHz", 88200),
        new("96 kHz", 96000)
    ];

    private static SelectionOption<int>[] GetCycleDurationOptions() =>
    [
        new("5 seconds", 5),
        new("8 seconds", 8),
        new("12 seconds", 12),
        new("20 seconds", 20),
        new("30 seconds", 30),
        new("45 seconds", 45),
        new("60 seconds", 60)
    ];

    private void CycleVisualizerIfDue()
    {
        if (!appSettings.EnableVisualizerAutoCycle)
            return;

        if (visualizerControl.UsesEmbeddedVisualizer)
            return;

        if (!engine.IsLoaded || cmbVisualizerMode.Items.Count <= 1)
            return;

        if (cmbVisualizerMode.DroppedDown)
        {
            ResetVisualizerCycleDeadline();
            return;
        }

        if (Environment.TickCount64 < nextVisualizerCycleTick)
            return;

        AdvanceVisualizerMode();
    }

    private void AdvanceVisualizerMode()
    {
        if (visualizerControl.UsesEmbeddedVisualizer || cmbVisualizerMode.Items.Count <= 1)
            return;

        cmbVisualizerMode.SelectedIndex = cmbVisualizerMode.SelectedIndex switch
        {
            < 0 => 0,
            var currentIndex => (currentIndex + 1) % cmbVisualizerMode.Items.Count
        };
    }

    private void ResetVisualizerCycleDeadline() =>
        nextVisualizerCycleTick = Environment.TickCount64 + (long)VisualizerAutoCycleInterval.TotalMilliseconds;

    private SelectionOption<VisualizerChoice> CreateEmbeddedVisualizerOption(VisualizerChoice fallbackChoice)
    {
        var displayName = engine.CurrentTrack?.EmbeddedVisualizer?.DisplayName;
        var label = string.IsNullOrWhiteSpace(displayName)
            ? "Embedded Visualizer"
            : $"Embedded: {displayName}";
        return new SelectionOption<VisualizerChoice>(label, fallbackChoice);
    }

    private void ShowSettingsDialog()
    {
        var currentTrack = engine.CurrentTrack;
        using var dialog = new SettingsDialog(
            appSettings,
            GetAvailableVisualizerModeOptions(),
            GetCurrentVisualizerChoice(),
            GetAllVisualizerModeOptions(),
            GetSampleRateOptions(),
            GetCycleDurationOptions(),
            currentTrack?.EmbeddedVisualizer?.DisplayName,
            currentTrack?.EmbeddedTheme?.DisplayName,
            GetEmbeddedContentDisplayName(currentTrack),
            spotifyService);

        var dialogResult = dialog.ShowDialog(this);
        if (dialogResult != DialogResult.OK)
            return;

        var previousClientId = SpotifyClientId;
        appSettings = AppSettingsStore.Normalize(dialog.Settings);
        SaveAppSettings();
        ApplyTheme();
        ApplyStoredSettings(dialog.SelectedCurrentVisualizer);
        UpdateUiState();

        if (spotifyService.IsLinked && SpotifyClientId != previousClientId)
            _ = ReloadSpotifyPlayerAsync();
        else if (spotifyService.IsLinked && spotifyWebView is null)
            _ = InitializeSpotifyAsync();
    }

    private VisualizerChoice GetCurrentVisualizerChoice() =>
        (cmbVisualizerMode.SelectedItem as SelectionOption<VisualizerChoice>)?.Value
        ?? GetDefaultVisualizerChoice();

    private VisualizerChoice GetDefaultVisualizerChoice() =>
        VisualizerChoice.FromSettingsKey(appSettings.DefaultVisualizerKey, appSettings.DefaultVisualizer);

    private VisualizerChoice GetPreferredVisualizerChoice(VisualizerChoice choice, bool hasAlbumArt, bool isMidi = false)
    {
        if (choice.TryGetInstalledId(out var installedId) &&
            redeemableVisualizers.TryGetInstalled(installedId, out _))
        {
            return choice;
        }

        var preferredMode = choice.TryGetBuiltInMode(out var mode)
            ? mode
            : VisualizerMode.MirrorSpectrum;
        return VisualizerChoice.BuiltIn(VisualizerCatalog.GetPreferredMode(preferredMode, hasAlbumArt, isMidi));
    }

    private SelectionOption<VisualizerChoice>[] GetVisualizerModeOptions(bool includeAlbumArtDependent, bool isMidi = false)
    {
        var builtInOptions = VisualizerCatalog
            .GetOptions(includeAlbumArtDependent, isMidi)
            .Select(static option => new SelectionOption<VisualizerChoice>(
                option.Label,
                VisualizerChoice.BuiltIn(option.Value)));

        var installedOptions = redeemableVisualizers.Installed
            .Select(static visualizer => new SelectionOption<VisualizerChoice>(
                $"Special: {visualizer.DisplayName}",
                VisualizerChoice.Installed(visualizer.Id)));

        var scriptedOptions = ScriptedVisualizerStore.LoadAll()
            .Select(static def => new SelectionOption<VisualizerChoice>(
                $"Script: {def.Name}",
                VisualizerChoice.Scripted(def.Id)));

        return builtInOptions.Concat(installedOptions).Concat(scriptedOptions).ToArray();
    }

    private void SaveAppSettings() => AppSettingsStore.Save(appSettings);

    private static string? GetEmbeddedContentDisplayName(AudioTrackInfo? track) =>
        track?.EmbeddedHtml?.DisplayName
        ?? track?.EmbeddedMarkdown?.DisplayName
        ?? track?.EmbeddedVideo?.DisplayName;

    private static void SelectComboValue<T>(ComboBox comboBox, T value)
    {
        for (var index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is SelectionOption<T> option &&
                EqualityComparer<T>.Default.Equals(option.Value, value))
            {
                comboBox.SelectedIndex = index;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;
    }
}
