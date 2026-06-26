using System.Drawing;

namespace Spectralis;

internal sealed class SettingsDialog : Form
{
    private readonly AppSettings workingSettings;
    private readonly List<SelectionOption<VisualizerChoice>> currentVisualizerOptions;
    private readonly List<SelectionOption<VisualizerChoice>> defaultVisualizerOptions;
    private readonly string? activeEmbeddedVisualizerLabel;
    private readonly string? activeEmbeddedThemeLabel;
    private readonly string? activeEmbeddedContentLabel;
    private readonly List<Panel> sectionPanels = [];
    private readonly List<Control> sectionSurfaceControls = [];
    private readonly List<Label> fieldTitleLabels = [];
    private readonly List<Label> fieldDescriptionLabels = [];
    private readonly List<Label> sectionTitleLabels = [];
    private readonly List<Label> sectionSubtitleLabels = [];

    private readonly TableLayoutPanel rootLayout;
    private readonly Panel headerPanel;
    private readonly Panel scrollContainer;
    private readonly StealthScrollPanel bodyFlow;
    private readonly ThemedScrollBar themedScrollBar;
    private readonly TableLayoutPanel footerLayout;
    private readonly Label lblTitle;
    private readonly Label lblSubtitle;
    private readonly Panel accentPreview;
    private readonly ModernComboBox cmbThemeMode;
    private readonly ModernComboBox cmbAccent;
    private readonly ModernComboBox cmbCurrentVisualizer;
    private readonly ModernComboBox cmbDefaultVisualizer;
    private readonly ModernComboBox cmbPlaybackRate;
    private readonly ModernComboBox cmbMidiInstrument;
    private readonly ModernComboBox cmbCycleDuration;
    private readonly ModernSwitch chkShowMoreInfo;
    private readonly ModernSwitch chkUseEmbeddedThemes;
    private readonly ModernSwitch chkPeakHold;
    private readonly ModernSwitch chkAutoCycle;
    private readonly ModernSwitch chkAutoPlayOnOpen;
    private readonly ModernSwitch chkQueueByDefault;
    private readonly ModernSwitch chkPreserveSession;
    private readonly ModernSwitch chkRememberWindowPlacement;
    private readonly ModernSwitch chkUseEmbeddedVisualizers;
    private readonly ModernSwitch chkUseEmbeddedContent;
    private readonly ModernSwitch chkClipboardUrlMonitoring;
    private readonly ModernSwitch chkDiscordRichPresence;
    private readonly ModernSwitch chkSharedPlay;
    private readonly ModernSwitch chkAutoUpdates;
    private readonly ModernSlider sldSensitivity;
    private readonly ModernSlider sldDefaultVolume;
    private readonly Label lblSensitivityValue;
    private readonly Label lblVolumeValue;
    private readonly ModernButton btnCancel;
    private readonly ModernButton btnSave;
    private readonly ModernButton btnSetDefaultApp;
    private readonly TextBox txtSpotifyClientId;
    private readonly ModernButton btnSpotifyLink;
    private readonly Label lblSpotifyStatus;
    private readonly SpotifyService spotifyService;
    private bool isSyncingScrollBar;

    public SettingsDialog(
        AppSettings currentSettings,
        IReadOnlyList<SelectionOption<VisualizerChoice>> currentVisualizerOptions,
        VisualizerChoice currentVisualizer,
        IReadOnlyList<SelectionOption<VisualizerChoice>> defaultVisualizerOptions,
        IReadOnlyList<SelectionOption<int>> sampleRateOptions,
        IReadOnlyList<SelectionOption<int>> cycleDurationOptions,
        string? activeEmbeddedVisualizerLabel,
        string? activeEmbeddedThemeLabel,
        string? activeEmbeddedContentLabel,
        SpotifyService spotifyService)
    {
        workingSettings = currentSettings.Clone();
        this.spotifyService = spotifyService;
        this.currentVisualizerOptions = currentVisualizerOptions.ToList();
        this.defaultVisualizerOptions = defaultVisualizerOptions.ToList();
        this.activeEmbeddedVisualizerLabel = string.IsNullOrWhiteSpace(activeEmbeddedVisualizerLabel)
            ? null
            : activeEmbeddedVisualizerLabel.Trim();
        this.activeEmbeddedThemeLabel = string.IsNullOrWhiteSpace(activeEmbeddedThemeLabel)
            ? null
            : activeEmbeddedThemeLabel.Trim();
        this.activeEmbeddedContentLabel = string.IsNullOrWhiteSpace(activeEmbeddedContentLabel)
            ? null
            : activeEmbeddedContentLabel.Trim();

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(840, 780);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        MinimumSize = new Size(680, 560);
        Padding = new Padding(0);
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Settings";
        HandleCreated += (_, _) =>
        {
            ApplyThemePreview();
            BeginInvoke(SyncScrollBar);
        };
        Resize += (_, _) =>
        {
            UpdateResponsiveLayout();
            SyncScrollBar();
        };

        rootLayout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 3
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle());
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        rootLayout.RowStyles.Add(new RowStyle());

        headerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 90,
            Margin = Padding.Empty,
            Padding = new Padding(28, 22, 28, 18)
        };

        lblTitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold, GraphicsUnit.Point),
            Location = new Point(0, 0),
            Text = "Settings"
        };

        lblSubtitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Location = new Point(0, 38),
            MaximumSize = new Size(760, 0),
            Text = "Appearance, playback, and visualizer defaults for new sessions and files opened through Spectralis."
        };

        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(lblSubtitle);

        bodyFlow = new StealthScrollPanel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Margin = Padding.Empty,
            Padding = new Padding(24, 0, 18, 0),
            WrapContents = false
        };
        bodyFlow.Layout += (_, _) => SyncScrollBar();
        bodyFlow.ScrollPositionChanged += (_, _) => SyncScrollBar();

        themedScrollBar = new ThemedScrollBar { Dock = DockStyle.Right, Width = 10 };
        themedScrollBar.Scroll += (_, _) =>
        {
            if (isSyncingScrollBar)
                return;

            isSyncingScrollBar = true;
            bodyFlow.AutoScrollPosition = new Point(0, themedScrollBar.Value);
            isSyncingScrollBar = false;
            BeginInvoke(SyncScrollBar);
        };

        scrollContainer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        scrollContainer.Controls.Add(bodyFlow);
        scrollContainer.Controls.Add(themedScrollBar);

        cmbThemeMode = CreateComboBox();
        cmbAccent = CreateComboBox();
        cmbCurrentVisualizer = CreateComboBox();
        cmbDefaultVisualizer = CreateComboBox();
        cmbPlaybackRate = CreateComboBox();
        cmbMidiInstrument = CreateComboBox();
        cmbCycleDuration = CreateComboBox();

        chkShowMoreInfo = CreateSwitch();
        chkUseEmbeddedThemes = CreateSwitch();
        chkPeakHold = CreateSwitch();
        chkAutoCycle = CreateSwitch();
        chkAutoPlayOnOpen = CreateSwitch();
        chkQueueByDefault = CreateSwitch();
        chkPreserveSession = CreateSwitch();
        chkRememberWindowPlacement = CreateSwitch();
        chkUseEmbeddedVisualizers = CreateSwitch();
        chkUseEmbeddedContent = CreateSwitch();
        chkClipboardUrlMonitoring = CreateSwitch();
        chkDiscordRichPresence = CreateSwitch();
        chkSharedPlay = CreateSwitch();
        chkAutoUpdates = CreateSwitch();

        sldSensitivity = CreateSlider();
        sldDefaultVolume = CreateSlider();

        lblSensitivityValue = CreateValueLabel();
        lblVolumeValue = CreateValueLabel();

        accentPreview = new Panel
        {
            Margin = new Padding(10, 0, 0, 0),
            Size = new Size(28, 28)
        };

        btnCancel = new ModernButton
        {
            Size = new Size(120, 42),
            Text = "Cancel"
        };
        btnCancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        btnSave = new ModernButton
        {
            Size = new Size(140, 42),
            Text = "Save Settings"
        };
        btnSave.Click += btnSave_Click;

        btnSetDefaultApp = new ModernButton
        {
            Size = new Size(158, 36),
            Text = "Set as Default..."
        };
        btnSetDefaultApp.Click += btnSetDefaultApp_Click;

        txtSpotifyClientId = new TextBox
        {
            Font = new Font("Segoe UI", 9F),
            Margin = Padding.Empty,
            MaxLength = 64,
            PlaceholderText = SpotifyClientIdProvider.HasDefaultClientId
                ? "Bundled Spotify app"
                : "Paste your Spotify Client ID...",
            Size = new Size(220, 28),
            Text = workingSettings.SpotifyClientId
        };

        btnSpotifyLink = new ModernButton
        {
            Size = new Size(220, 36),
            Text = spotifyService.IsLinked ? "Unlink Account" : "Link Account"
        };
        btnSpotifyLink.Click += BtnSpotifyLink_Click;

        lblSpotifyStatus = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.25F),
            Margin = new Padding(0, 4, 0, 0),
            Text = BuildSpotifyStatusText()
        };
        sectionSurfaceControls.Add(lblSpotifyStatus);

        var appearanceSection = CreateSection(
            "Appearance",
            "Change the shell theme and how much metadata stays visible by default.");
        appearanceSection.Controls.Add(CreateFieldRow("Theme mode", "Switch between the dark and light shell.", cmbThemeMode));
        appearanceSection.Controls.Add(CreateFieldRow("Theme accent", "Choose the accent color used across buttons, menus, and highlights.", CreateAccentControlHost()));
        appearanceSection.Controls.Add(CreateFieldRow("Use embedded track themes", GetEmbeddedThemeDescription(), chkUseEmbeddedThemes));
        appearanceSection.Controls.Add(CreateFieldRow("Show more info", "Displays the extra artist, format, and duration line under the track title.", chkShowMoreInfo));

        var visualizerSection = CreateSection(
            "Visualizer",
            "Set how the visualizer behaves right now and when new tracks are opened.");
        visualizerSection.Controls.Add(CreateFieldRow("Current visualizer", "Applies immediately to the track that's already loaded.", cmbCurrentVisualizer));
        visualizerSection.Controls.Add(CreateFieldRow("Default visualizer", "Used for newly loaded tracks. Album-art visualizers fall back when no album cover exists.", cmbDefaultVisualizer));
        visualizerSection.Controls.Add(CreateFieldRow("Use embedded track visualizers", GetEmbeddedVisualizerDescription(), chkUseEmbeddedVisualizers));
        visualizerSection.Controls.Add(CreateFieldRow("Use embedded track content", GetEmbeddedContentDescription(), chkUseEmbeddedContent));
        visualizerSection.Controls.Add(CreateFieldRow("Auto-cycle visualizers", "Rotates through the available visualizers while a track is loaded.", chkAutoCycle));
        visualizerSection.Controls.Add(CreateFieldRow("Cycle duration", "How long each visualizer stays on screen before rotating.", cmbCycleDuration));
        visualizerSection.Controls.Add(CreateFieldRow("Peak hold", "Keeps peak markers visible for a brief moment in spectrum views.", chkPeakHold));
        visualizerSection.Controls.Add(CreateFieldRow("Sensitivity", "Controls how aggressively the visualizer reacts to quieter material.", CreateSliderHost(sldSensitivity, lblSensitivityValue)));

        var playbackSection = CreateSection(
            "Playback",
            "Defaults applied to new sessions and files opened through Windows file associations.");
        playbackSection.Controls.Add(CreateFieldRow("Default playback rate", "Preferred output sample rate. Match source leaves the file unchanged.", cmbPlaybackRate));
        playbackSection.Controls.Add(CreateFieldRow("MIDI instrument", "SoundFont instrument used for MIDI files. Use MIDI File preserves embedded program changes.", cmbMidiInstrument));
        playbackSection.Controls.Add(CreateFieldRow("Default volume", "Starting playback volume for a new session.", CreateSliderHost(sldDefaultVolume, lblVolumeValue)));
        playbackSection.Controls.Add(CreateFieldRow("Autoplay on open", "Starts playback automatically after opening a file. This is on by default.", chkAutoPlayOnOpen));
        playbackSection.Controls.Add(CreateFieldRow("Queue by default", "Dropped and externally opened tracks are queued instead of replacing the current session.", chkQueueByDefault));
        playbackSection.Controls.Add(CreateFieldRow("Preserve session", "Files and links opened from Windows or the browser reuse this window. This is on by default.", chkPreserveSession));
        playbackSection.Controls.Add(CreateFieldRow("Remember window placement", "Restores the last window size, position, and maximized state on startup.", chkRememberWindowPlacement));

        var integrationSection = CreateSection(
            "Integration",
            "Windows-level and account-level behaviors that affect how Spectralis shares and opens files.");
        integrationSection.Controls.Add(CreateFieldRow("Clipboard URL monitoring", "Shows a play prompt when a supported track URL is copied.", chkClipboardUrlMonitoring));
        integrationSection.Controls.Add(CreateFieldRow("Discord rich presence", "Shows the current track as a Discord Listening status when Discord is running.", chkDiscordRichPresence));
        integrationSection.Controls.Add(CreateFieldRow("Shared Play", "Opt in to private Spectralis sessions that temporarily upload a rich cached copy of the playing track.", chkSharedPlay));
        integrationSection.Controls.Add(CreateFieldRow("Automatic updates", "Downloads and installs Spectralis updates on startup. When off, Spectralis asks first.", chkAutoUpdates));
        integrationSection.Controls.Add(CreateFieldRow("Default app", "Registers Spectralis for supported audio extensions, then opens Windows Default Apps to confirm.", btnSetDefaultApp));

        var spotifySection = CreateSection(
            "Spotify",
            GetSpotifySectionDescription());
        var showSpotifyClientIdField =
            !SpotifyClientIdProvider.HasDefaultClientId ||
            !string.IsNullOrWhiteSpace(workingSettings.SpotifyClientId);
        if (showSpotifyClientIdField)
            spotifySection.Controls.Add(CreateFieldRow("Client ID", GetSpotifyClientIdDescription(), txtSpotifyClientId));
        spotifySection.Controls.Add(CreateSpotifyLinkRow());

        bodyFlow.Controls.Add(appearanceSection);
        bodyFlow.Controls.Add(visualizerSection);
        bodyFlow.Controls.Add(playbackSection);
        bodyFlow.Controls.Add(integrationSection);
        bodyFlow.Controls.Add(spotifySection);

        footerLayout = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Height = 82,
            Margin = Padding.Empty,
            Padding = new Padding(28, 18, 28, 22),
            RowCount = 1
        };
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        btnCancel.Margin = new Padding(0, 0, 10, 0);
        btnSave.Margin = Padding.Empty;

        footerLayout.Controls.Add(btnCancel, 1, 0);
        footerLayout.Controls.Add(btnSave, 2, 0);

        rootLayout.Controls.Add(headerPanel, 0, 0);
        rootLayout.Controls.Add(scrollContainer, 0, 1);
        rootLayout.Controls.Add(footerLayout, 0, 2);

        Controls.Add(rootLayout);

        PopulateComboOptions(currentVisualizer, defaultVisualizerOptions, sampleRateOptions, cycleDurationOptions);
        WireEvents();
        UpdateResponsiveLayout();
        ApplyThemePreview();
    }

    public AppSettings Settings => workingSettings.Clone();

    public VisualizerChoice SelectedCurrentVisualizer { get; private set; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            case Keys.Enter:
                btnSave_Click(this, EventArgs.Empty);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private string GetEmbeddedThemeDescription() =>
        activeEmbeddedThemeLabel is null
            ? "If a track embeds a theme, the player can lock the shell to it until the track changes."
            : $"The current track embeds the theme '{activeEmbeddedThemeLabel}'. Turn this off to keep using the app theme instead.";

    private string GetEmbeddedVisualizerDescription() =>
        activeEmbeddedVisualizerLabel is null
            ? "If a track embeds a visualizer, the player can lock playback to it instead of built-in visualizers."
            : $"The current track embeds the visualizer '{activeEmbeddedVisualizerLabel}'. Turn this off to pick a built-in visualizer instead.";

    private string GetEmbeddedContentDescription() =>
        activeEmbeddedContentLabel is null
            ? "If a track embeds HTML, Markdown, or video, the player can show it in the visualizer area."
            : $"The current track embeds '{activeEmbeddedContentLabel}'. Turn this off to keep the built-in visualizer visible.";

    private static string GetSpotifySectionDescription() =>
        SpotifyClientIdProvider.HasDefaultClientId
            ? "Stream Spotify audio through Spectralis and its visualizer. Requires Spotify Premium."
            : "Stream Spotify audio through Spectralis and its visualizer. Requires Spotify Premium and a registered Spotify Developer App with redirect URI: http://127.0.0.1:5127/callback";

    private static string GetSpotifyClientIdDescription() =>
        SpotifyClientIdProvider.HasDefaultClientId
            ? "Optional custom Spotify app Client ID. Leave blank to use the bundled Spectralis app."
            : "Your Spotify Developer App's Client ID. Create one at developer.spotify.com.";

    private void PopulateComboOptions(
        VisualizerChoice currentVisualizer,
        IReadOnlyList<SelectionOption<VisualizerChoice>> defaultVisualizerOptions,
        IReadOnlyList<SelectionOption<int>> sampleRateOptions,
        IReadOnlyList<SelectionOption<int>> cycleDurationOptions)
    {
        cmbThemeMode.Items.AddRange(
            [
                new SelectionOption<ThemeMode>("Dark",     ThemeMode.Dark),
                new SelectionOption<ThemeMode>("Light",    ThemeMode.Light),
                new SelectionOption<ThemeMode>("OLED",     ThemeMode.Oled),
                new SelectionOption<ThemeMode>("Midnight", ThemeMode.Midnight)
            ]);
        cmbAccent.Items.AddRange(
            [
                new SelectionOption<ThemeAccent>("Amber",   ThemeAccent.Amber),
                new SelectionOption<ThemeAccent>("Ocean",   ThemeAccent.Ocean),
                new SelectionOption<ThemeAccent>("Rose",    ThemeAccent.Rose),
                new SelectionOption<ThemeAccent>("Forest",  ThemeAccent.Forest),
                new SelectionOption<ThemeAccent>("Violet",  ThemeAccent.Violet),
                new SelectionOption<ThemeAccent>("Crimson", ThemeAccent.Crimson),
                new SelectionOption<ThemeAccent>("Cyan",    ThemeAccent.Cyan),
                new SelectionOption<ThemeAccent>("Mint",    ThemeAccent.Mint),
                new SelectionOption<ThemeAccent>("Sunset",  ThemeAccent.Sunset),
                new SelectionOption<ThemeAccent>("Gold",    ThemeAccent.Gold)
            ]);
        cmbDefaultVisualizer.Items.AddRange(defaultVisualizerOptions.Select(static option => (object)option).ToArray());
        cmbPlaybackRate.Items.AddRange(sampleRateOptions.Select(static option => (object)option).ToArray());
        cmbMidiInstrument.Items.AddRange(MidiPlaybackInstrumentCatalog.GetOptions().Select(static option => (object)option).ToArray());
        cmbCycleDuration.Items.AddRange(cycleDurationOptions.Select(static option => (object)option).ToArray());

        SelectComboValue(cmbThemeMode, workingSettings.ThemeMode);
        SelectComboValue(cmbAccent, workingSettings.ThemeAccent);
        SelectComboValue(
            cmbDefaultVisualizer,
            VisualizerChoice.FromSettingsKey(workingSettings.DefaultVisualizerKey, workingSettings.DefaultVisualizer));
        SelectComboValue(cmbPlaybackRate, workingSettings.PreferredSampleRate);
        SelectComboValue(cmbMidiInstrument, workingSettings.MidiInstrument);
        SelectComboValue(cmbCycleDuration, workingSettings.VisualizerCycleSeconds);

        chkShowMoreInfo.Checked = workingSettings.ShowMoreInfo;
        chkUseEmbeddedThemes.Checked = workingSettings.UseEmbeddedTrackThemes;
        chkPeakHold.Checked = workingSettings.PeakHold;
        chkAutoCycle.Checked = workingSettings.EnableVisualizerAutoCycle;
        chkAutoPlayOnOpen.Checked = workingSettings.AutoPlayOnOpen;
        chkQueueByDefault.Checked = workingSettings.QueueByDefault;
        chkPreserveSession.Checked = workingSettings.PreserveSession;
        chkRememberWindowPlacement.Checked = workingSettings.RememberWindowPlacement;
        chkUseEmbeddedVisualizers.Checked = workingSettings.UseEmbeddedTrackVisualizers;
        chkUseEmbeddedContent.Checked = workingSettings.UseEmbeddedTrackContent;
        chkClipboardUrlMonitoring.Checked = workingSettings.EnableClipboardUrlMonitoring;
        chkDiscordRichPresence.Checked = workingSettings.EnableDiscordRichPresence;
        chkSharedPlay.Checked = workingSettings.EnableSharedPlay;
        chkAutoUpdates.Checked = workingSettings.EnableAutoUpdates;

        sldSensitivity.Minimum = 50;
        sldSensitivity.Maximum = 200;
        sldSensitivity.Value = workingSettings.VisualizerSensitivity;
        sldDefaultVolume.Minimum = 0;
        sldDefaultVolume.Maximum = 100;
        sldDefaultVolume.Value = workingSettings.DefaultVolume;
        UpdateSliderLabels();
        UpdateCycleDurationState();
        SelectedCurrentVisualizer = currentVisualizer;
        RefreshCurrentVisualizerOptions();
    }

    private void WireEvents()
    {
        cmbThemeMode.SelectedIndexChanged += (_, _) => ApplyThemePreview();
        cmbAccent.SelectedIndexChanged += (_, _) => ApplyThemePreview();
        cmbCurrentVisualizer.SelectedIndexChanged += (_, _) =>
            SelectedCurrentVisualizer = GetSelectedValue(cmbCurrentVisualizer, SelectedCurrentVisualizer);
        chkAutoCycle.CheckedChanged += (_, _) => UpdateCycleDurationState();
        chkUseEmbeddedVisualizers.CheckedChanged += (_, _) => RefreshCurrentVisualizerOptions();
        sldSensitivity.Scroll += (_, _) => UpdateSliderLabels();
        sldDefaultVolume.Scroll += (_, _) => UpdateSliderLabels();
    }

    private void UpdateSliderLabels()
    {
        lblSensitivityValue.Text = $"{sldSensitivity.Value}%";
        lblVolumeValue.Text = $"{sldDefaultVolume.Value}%";
    }

    private void UpdateCycleDurationState()
    {
        cmbCycleDuration.Enabled = chkAutoCycle.Checked;
    }

    private void RefreshCurrentVisualizerOptions()
    {
        var isLockedToEmbedded = chkUseEmbeddedVisualizers.Checked && activeEmbeddedVisualizerLabel is not null;

        cmbCurrentVisualizer.BeginUpdate();
        try
        {
            cmbCurrentVisualizer.Items.Clear();
            if (isLockedToEmbedded)
            {
                cmbCurrentVisualizer.Items.Add(new SelectionOption<VisualizerChoice>(
                    $"Embedded: {activeEmbeddedVisualizerLabel}",
                    SelectedCurrentVisualizer));
                cmbCurrentVisualizer.SelectedIndex = 0;
                cmbCurrentVisualizer.Enabled = false;
                return;
            }

            cmbCurrentVisualizer.Items.AddRange(currentVisualizerOptions.Select(static option => (object)option).ToArray());
            SelectComboValue(cmbCurrentVisualizer, SelectedCurrentVisualizer);
            cmbCurrentVisualizer.Enabled = cmbCurrentVisualizer.Items.Count > 1;
        }
        finally
        {
            cmbCurrentVisualizer.EndUpdate();
        }
    }

    private void ApplyThemePreview()
    {
        var palette = ThemePalette.Create(
            GetSelectedValue(cmbThemeMode, workingSettings.ThemeMode),
            GetSelectedValue(cmbAccent, workingSettings.ThemeAccent));

        WindowChromeStyler.ApplyTheme(this, palette);
        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;
        rootLayout.BackColor = palette.WindowBackColor;
        headerPanel.BackColor = palette.WindowBackColor;
        bodyFlow.BackColor = palette.WindowBackColor;
        scrollContainer.BackColor = palette.WindowBackColor;
        themedScrollBar.ApplyTheme(palette);
        footerLayout.BackColor = palette.WindowBackColor;
        lblTitle.ForeColor = palette.TextPrimaryColor;
        lblSubtitle.ForeColor = palette.TextSecondaryColor;

        foreach (var section in sectionPanels)
            section.BackColor = palette.SurfaceBackColor;

        foreach (var control in sectionSurfaceControls)
            control.BackColor = palette.SurfaceBackColor;

        foreach (var label in sectionTitleLabels)
            label.ForeColor = palette.TextPrimaryColor;

        foreach (var label in sectionSubtitleLabels)
            label.ForeColor = palette.TextMutedColor;

        foreach (var label in fieldTitleLabels)
            label.ForeColor = palette.TextPrimaryColor;

        foreach (var label in fieldDescriptionLabels)
            label.ForeColor = palette.TextMutedColor;

        lblSensitivityValue.ForeColor = palette.TextSecondaryColor;
        lblVolumeValue.ForeColor = palette.TextSecondaryColor;

        ThemeControlStyler.ApplyComboBoxTheme(cmbThemeMode, palette);
        ThemeControlStyler.ApplyComboBoxTheme(cmbAccent, palette);
        ThemeControlStyler.ApplyComboBoxTheme(cmbCurrentVisualizer, palette);
        ThemeControlStyler.ApplyComboBoxTheme(cmbDefaultVisualizer, palette);
        ThemeControlStyler.ApplyComboBoxTheme(cmbPlaybackRate, palette);
        ThemeControlStyler.ApplyComboBoxTheme(cmbMidiInstrument, palette);
        ThemeControlStyler.ApplyComboBoxTheme(cmbCycleDuration, palette);

        ThemeControlStyler.ApplySliderTheme(sldSensitivity, palette);
        ThemeControlStyler.ApplySliderTheme(sldDefaultVolume, palette);
        ThemeControlStyler.ApplySwitchTheme(chkShowMoreInfo, palette);
        ThemeControlStyler.ApplySwitchTheme(chkUseEmbeddedThemes, palette);
        ThemeControlStyler.ApplySwitchTheme(chkPeakHold, palette);
        ThemeControlStyler.ApplySwitchTheme(chkAutoCycle, palette);
        ThemeControlStyler.ApplySwitchTheme(chkAutoPlayOnOpen, palette);
        ThemeControlStyler.ApplySwitchTheme(chkQueueByDefault, palette);
        ThemeControlStyler.ApplySwitchTheme(chkPreserveSession, palette);
        ThemeControlStyler.ApplySwitchTheme(chkRememberWindowPlacement, palette);
        ThemeControlStyler.ApplySwitchTheme(chkUseEmbeddedVisualizers, palette);
        ThemeControlStyler.ApplySwitchTheme(chkUseEmbeddedContent, palette);
        ThemeControlStyler.ApplySwitchTheme(chkClipboardUrlMonitoring, palette);
        ThemeControlStyler.ApplySwitchTheme(chkDiscordRichPresence, palette);
        ThemeControlStyler.ApplySwitchTheme(chkSharedPlay, palette);
        ThemeControlStyler.ApplySwitchTheme(chkAutoUpdates, palette);

        ThemeControlStyler.ApplyGhostButtonTheme(btnCancel, palette, palette.BorderStrongColor);
        ThemeControlStyler.ApplyPrimaryButtonTheme(btnSave, palette, palette.AccentPrimaryColor);
        ThemeControlStyler.ApplyGhostButtonTheme(btnSetDefaultApp, palette, palette.AccentSoftColor);

        if (spotifyService.IsLinked)
            ThemeControlStyler.ApplyGhostButtonTheme(btnSpotifyLink, palette, palette.DangerColor);
        else
            ThemeControlStyler.ApplyPrimaryButtonTheme(btnSpotifyLink, palette, palette.AccentPrimaryColor);

        txtSpotifyClientId.BackColor = palette.SurfaceAltBackColor;
        txtSpotifyClientId.ForeColor = palette.TextPrimaryColor;
        txtSpotifyClientId.BorderStyle = BorderStyle.FixedSingle;
        lblSpotifyStatus.ForeColor = palette.TextMutedColor;

        accentPreview.BackColor = palette.AccentPrimaryColor;
        accentPreview.BorderStyle = BorderStyle.FixedSingle;
    }

    private void btnSave_Click(object? sender, EventArgs e)
    {
        workingSettings.ThemeMode = GetSelectedValue(cmbThemeMode, ThemeMode.Dark);
        workingSettings.ThemeAccent = GetSelectedValue(cmbAccent, ThemeAccent.Amber);
        workingSettings.UseEmbeddedTrackThemes = chkUseEmbeddedThemes.Checked;
        var defaultVisualizerChoice = GetSelectedValue(
            cmbDefaultVisualizer,
            VisualizerChoice.BuiltIn(VisualizerMode.MirrorSpectrum));
        workingSettings.DefaultVisualizerKey = defaultVisualizerChoice.ToSettingsKey();
        workingSettings.DefaultVisualizer = defaultVisualizerChoice.FallbackMode;
        workingSettings.UseEmbeddedTrackVisualizers = chkUseEmbeddedVisualizers.Checked;
        workingSettings.UseEmbeddedTrackContent = chkUseEmbeddedContent.Checked;
        workingSettings.PreferredSampleRate = GetSelectedValue(cmbPlaybackRate, 0);
        workingSettings.MidiInstrument = GetSelectedValue(cmbMidiInstrument, MidiPlaybackInstrument.AcousticGrandPiano);
        workingSettings.DefaultVolume = sldDefaultVolume.Value;
        workingSettings.PeakHold = chkPeakHold.Checked;
        workingSettings.VisualizerSensitivity = sldSensitivity.Value;
        workingSettings.EnableVisualizerAutoCycle = chkAutoCycle.Checked;
        workingSettings.VisualizerCycleSeconds = GetSelectedValue(cmbCycleDuration, 12);
        workingSettings.AutoPlayOnOpen = chkAutoPlayOnOpen.Checked;
        workingSettings.QueueByDefault = chkQueueByDefault.Checked;
        workingSettings.PreserveSession = chkPreserveSession.Checked;
        workingSettings.RememberWindowPlacement = chkRememberWindowPlacement.Checked;
        workingSettings.ShowMoreInfo = chkShowMoreInfo.Checked;
        workingSettings.EnableClipboardUrlMonitoring = chkClipboardUrlMonitoring.Checked;
        workingSettings.EnableDiscordRichPresence = chkDiscordRichPresence.Checked;
        workingSettings.EnableSharedPlay = chkSharedPlay.Checked;
        workingSettings.EnableAutoUpdates = chkAutoUpdates.Checked;
        workingSettings.SpotifyClientId = txtSpotifyClientId.Text.Trim();
        SelectedCurrentVisualizer = GetSelectedValue(
            cmbCurrentVisualizer,
            VisualizerChoice.BuiltIn(VisualizerMode.MirrorSpectrum));

        DialogResult = DialogResult.OK;
        Close();
    }

    private void btnSetDefaultApp_Click(object? sender, EventArgs e)
    {
        try
        {
            DefaultAppRegistrar.RegisterCurrentUser();
            DefaultAppRegistrar.OpenDefaultAppsSettings();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Spectralis could not register itself for Windows file associations.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Registration Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async void BtnSpotifyLink_Click(object? sender, EventArgs e)
    {
        if (spotifyService.IsLinked)
        {
            spotifyService.UnlinkAccount();
            btnSpotifyLink.Text = "Link Account";
            lblSpotifyStatus.Text = BuildSpotifyStatusText();
            ApplyThemePreview();
            return;
        }

        var clientId = SpotifyClientIdProvider.ResolveClientId(txtSpotifyClientId.Text);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            MessageBox.Show(this, "Spotify is not configured for this build. Enter your Spotify Client ID first.", "Spotify", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        btnSpotifyLink.Enabled = false;
        btnSpotifyLink.Text = "Waiting for browser...";
        workingSettings.SpotifyClientId = txtSpotifyClientId.Text.Trim();

        try
        {
            var success = await spotifyService.LinkAccountAsync(clientId, this);
            btnSpotifyLink.Text = success ? "Unlink Account" : "Link Account";
            lblSpotifyStatus.Text = BuildSpotifyStatusText();
            ApplyThemePreview();

            if (!success)
                MessageBox.Show(this, "Spotify account linking failed or timed out.", "Spotify", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            btnSpotifyLink.Enabled = true;
        }
    }

    private string BuildSpotifyStatusText()
    {
        if (!spotifyService.IsLinked) return "Not linked";
        var name = spotifyService.AccountDisplayName ?? spotifyService.AccountEmail ?? "Unknown";
        return $"Linked: {name}";
    }

    private Control CreateSpotifyLinkRow()
    {
        var host = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0, 0, 0, 14),
            MaximumSize = new Size(590, 0),
            MinimumSize = new Size(590, 0),
            Padding = Padding.Empty,
            RowCount = 2
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        host.RowStyles.Add(new RowStyle());
        host.RowStyles.Add(new RowStyle());

        var leftStack = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 2
        };
        leftStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.25F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = Padding.Empty,
            Text = "Link account"
        };
        fieldTitleLabels.Add(titleLabel);

        var descLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 5, 0, 0),
            MaximumSize = new Size(440, 0),
            Text = "Authenticate with Spotify to enable playback through Spectralis."
        };
        fieldDescriptionLabels.Add(descLabel);

        leftStack.Controls.Add(titleLabel, 0, 0);
        leftStack.Controls.Add(lblSpotifyStatus, 0, 1);
        leftStack.Controls.Add(descLabel, 0, 2);

        var row = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 14),
            MaximumSize = new Size(590, 0),
            MinimumSize = new Size(590, 0),
            Padding = Padding.Empty,
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280F));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        btnSpotifyLink.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        btnSpotifyLink.Margin = Padding.Empty;

        row.Controls.Add(leftStack, 0, 0);
        row.Controls.Add(btnSpotifyLink, 1, 0);
        sectionSurfaceControls.Add(row);
        sectionSurfaceControls.Add(leftStack);
        return row;
    }

    private static ModernComboBox CreateComboBox() =>
        new()
        {
            Margin = Padding.Empty,
            Size = new Size(220, 36)
        };

    private static ModernSlider CreateSlider() =>
        new()
        {
            Dock = DockStyle.Fill,
            IsLarge = false,
            Margin = Padding.Empty,
            Size = new Size(164, 32)
        };

    private static ModernSwitch CreateSwitch() =>
        new()
        {
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty
        };

    private static Label CreateValueLabel() =>
        new()
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 8.75F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(10, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleRight,
            Width = 46
        };

    private FlowLayoutPanel CreateSection(string title, string subtitle)
    {
        var section = new FlowLayoutPanel
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 14),
            Padding = new Padding(18, 18, 18, 18),
            FlowDirection = FlowDirection.TopDown,
            Size = new Size(646, 10),
            WrapContents = false
        };

        var lblSectionTitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = Padding.Empty,
            Text = title
        };
        sectionTitleLabels.Add(lblSectionTitle);

        var lblSectionSubtitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 6, 0, 14),
            MaximumSize = new Size(580, 0),
            Text = subtitle
        };
        sectionSubtitleLabels.Add(lblSectionSubtitle);

        section.Controls.Add(lblSectionTitle);
        section.Controls.Add(lblSectionSubtitle);
        sectionPanels.Add(section);
        sectionSurfaceControls.Add(section);
        return section;
    }

    private Control CreateFieldRow(string title, string description, Control control)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 14),
            MaximumSize = new Size(590, 0),
            MinimumSize = new Size(590, 0),
            Padding = Padding.Empty,
            RowCount = 1
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280F));

        var labelStack = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 2
        };
        labelStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        labelStack.RowStyles.Add(new RowStyle());
        labelStack.RowStyles.Add(new RowStyle());

        var lblTitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.25F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = Padding.Empty,
            Text = title
        };
        fieldTitleLabels.Add(lblTitle);

        var lblDescription = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 5, 0, 0),
            MaximumSize = new Size(440, 0),
            Text = description
        };
        fieldDescriptionLabels.Add(lblDescription);

        labelStack.Controls.Add(lblTitle, 0, 0);
        labelStack.Controls.Add(lblDescription, 0, 1);

        // Don't stretch switches - they have a fixed size
        if (control is not ModernSwitch)
        {
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        }
        else
        {
            control.Anchor = AnchorStyles.Left;
        }
        control.Margin = Padding.Empty;

        row.Controls.Add(labelStack, 0, 0);
        row.Controls.Add(control, 1, 0);
        sectionSurfaceControls.Add(row);
        sectionSurfaceControls.Add(labelStack);
        return row;
    }

    private Control CreateAccentControlHost()
    {
        cmbAccent.Width = 182;

        var host = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            WrapContents = false
        };

        host.Controls.Add(cmbAccent);
        host.Controls.Add(accentPreview);
        sectionSurfaceControls.Add(host);
        return host;
    }

    private Control CreateSliderHost(ModernSlider slider, Label valueLabel)
    {
        var host = new TableLayoutPanel
        {
            ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 1,
            Size = new Size(220, 36)
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52F));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        host.Controls.Add(slider, 0, 0);
        host.Controls.Add(valueLabel, 1, 0);
        sectionSurfaceControls.Add(host);
        return host;
    }

    private static void SelectComboValue<T>(ModernComboBox comboBox, T value)
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

    private static T GetSelectedValue<T>(ModernComboBox comboBox, T fallback) =>
        comboBox.SelectedItem is SelectionOption<T> option ? option.Value : fallback;

    private void SyncScrollBar()
    {
        if (!IsHandleCreated) return;
        if (isSyncingScrollBar) return;

        isSyncingScrollBar = true;
        var scrollPosition = Math.Abs(bodyFlow.AutoScrollPosition.Y);
        var bottom = 0;
        foreach (Control c in bodyFlow.Controls)
            bottom = Math.Max(bottom, c.Top + scrollPosition + c.Height);
        bottom += bodyFlow.Padding.Top + bodyFlow.Padding.Bottom;

        var viewH = bodyFlow.ClientSize.Height;

        themedScrollBar.Maximum = Math.Max(bottom, viewH);
        themedScrollBar.LargeChange = Math.Max(1, viewH);
        themedScrollBar.Value = scrollPosition;
        themedScrollBar.Visible = bottom > viewH;
        isSyncingScrollBar = false;
    }

    private void UpdateResponsiveLayout()
    {
        if (bodyFlow is null)
            return;

        var availableWidth = Math.Max(420, bodyFlow.ClientSize.Width - bodyFlow.Padding.Left - bodyFlow.Padding.Right - 6);
        var sectionWidth = Math.Min(760, availableWidth);
        var rowWidth = Math.Max(320, sectionWidth - 36);
        var controlColumnWidth = Math.Clamp((int)(rowWidth * 0.38f), 220, 280);
        var labelWidth = Math.Max(220, rowWidth - controlColumnWidth - 8);

        foreach (var section in sectionPanels)
        {
            section.Width = sectionWidth;
            section.MinimumSize = new Size(sectionWidth, 0);
            section.MaximumSize = new Size(sectionWidth, 0);
        }

        foreach (Control control in bodyFlow.Controls)
            ApplyResponsiveSizing(control, rowWidth, controlColumnWidth, labelWidth);
    }

    private static void ApplyResponsiveSizing(
        Control control,
        int rowWidth,
        int controlColumnWidth,
        int labelWidth)
    {
        if (control is TableLayoutPanel table &&
            (table.MinimumSize.Width >= 500 || table.MaximumSize.Width >= 500))
        {
            table.Width = rowWidth;
            table.MinimumSize = new Size(rowWidth, 0);
            table.MaximumSize = new Size(rowWidth, 0);

            if (table.ColumnStyles.Count >= 2)
                table.ColumnStyles[1].Width = controlColumnWidth;
        }

        if (control is Label label && label.AutoSize)
            label.MaximumSize = new Size(labelWidth, 0);

        foreach (Control child in control.Controls)
            ApplyResponsiveSizing(child, rowWidth, controlColumnWidth, labelWidth);
    }

    private sealed class StealthScrollPanel : FlowLayoutPanel
    {
        private const int SB_VERT = 1;
        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCPAINT = 0x0085;
        private const int WM_VSCROLL = 0x0115;
        private const int WM_MOUSEWHEEL = 0x020A;

        public event EventHandler? ScrollPositionChanged;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            switch (m.Msg)
            {
                case WM_NCCALCSIZE:
                case WM_NCPAINT:
                    HideNativeScrollBar();
                    break;
                case WM_VSCROLL:
                case WM_MOUSEWHEEL:
                    ScrollPositionChanged?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            HideNativeScrollBar();
        }

        private void HideNativeScrollBar()
        {
            if (IsHandleCreated)
                NativeMethods.ShowScrollBar(Handle, SB_VERT, false);
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public static extern bool ShowScrollBar(IntPtr hWnd, int wBar,
                [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] bool bShow);
        }
    }
}
