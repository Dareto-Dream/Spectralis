using System.Drawing;

namespace Spectralis;

internal sealed class ObsDialog : Form
{
    private readonly AppSettings settings;
    private readonly ObsOverlayServer? server;
    private readonly ModernSwitch chkEnabled;
    private readonly TextBox txtUrl;
    private readonly ModernButton btnCopyUrl;
    private readonly ModernButton btnRegenToken;
    private readonly ModernComboBox cmbPreset;
    private readonly ModernSwitch chkDesignerProfile;
    private readonly ModernSwitch chkNowPlaying;
    private readonly ModernSwitch chkLyrics;
    private readonly ModernSwitch chkVisualizer;
    private readonly ModernSwitch chkQueue;
    private readonly ModernSwitch chkProgress;
    private readonly ModernSwitch chkNextLyric;
    private readonly ModernComboBox cmbArtworkShape;
    private readonly ModernSlider sldOpacity;
    private readonly ModernSlider sldScale;
    private readonly ModernSlider sldRadius;
    private readonly ModernSlider sldVisualizerIntensity;
    private readonly Label lblOpacityValue;
    private readonly Label lblScaleValue;
    private readonly Label lblRadiusValue;
    private readonly Label lblVisualizerIntensityValue;
    private readonly PresetPreview preview;
    private readonly Label lblStatus;
    private Control? urlRow;

    public ObsDialog(AppSettings settings, ObsOverlayServer? server)
    {
        this.settings = settings;
        this.server = server;

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(760, 560);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        MinimumSize = new Size(720, 520);
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "OBS Overlay";

        var palette = ThemePalette.Create(settings.ThemeMode, settings.ThemeAccent);
        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;
        WindowChromeStyler.ApplyTheme(this, palette);

        chkEnabled = new ModernSwitch { Anchor = AnchorStyles.Left };
        chkEnabled.Checked = settings.EnableObsOverlay;
        ThemeControlStyler.ApplySwitchTheme(chkEnabled, palette);

        txtUrl = new TextBox
        {
            Font = new Font("Consolas", 9F),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            ReadOnly = true,
            Size = new Size(490, 28),
            Text = server?.BaseUrl ?? BuildPreviewUrl(settings)
        };
        txtUrl.BackColor = palette.SurfaceAltBackColor;
        txtUrl.ForeColor = palette.TextPrimaryColor;

        btnCopyUrl = new ModernButton { Anchor = AnchorStyles.Right, Size = new Size(96, 36), Text = "Copy URL" };
        ThemeControlStyler.ApplyPrimaryButtonTheme(btnCopyUrl, palette, palette.AccentPrimaryColor);
        btnCopyUrl.Click += (_, _) => CopyUrl();

        btnRegenToken = new ModernButton { Anchor = AnchorStyles.Right, Size = new Size(140, 36), Text = "New Token" };
        ThemeControlStyler.ApplyGhostButtonTheme(btnRegenToken, palette, palette.DangerColor);
        btnRegenToken.Click += (_, _) => RegenToken();

        cmbPreset = new ModernComboBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Size = new Size(260, 36) };
        ThemeControlStyler.ApplyComboBoxTheme(cmbPreset, palette);
        cmbPreset.Items.AddRange([
            new SelectionOption<string>("Compact Corner", "compact"),
            new SelectionOption<string>("Lyrics Lower Third", "lyrics-lower-third"),
            new SelectionOption<string>("Full Visualizer", "full-visualizer"),
            new SelectionOption<string>("Queue Sidebar", "queue-sidebar"),
            new SelectionOption<string>("Vertical Stream", "vertical-stream"),
            new SelectionOption<string>("Capsule Mode", "capsule-mode"),
            new SelectionOption<string>("Minimal Ticker", "minimal-ticker"),
            new SelectionOption<string>("Album Card", "album-card"),
            new SelectionOption<string>("Lyrics Focus", "lyrics-focus"),
            new SelectionOption<string>("Visualizer Strip", "visualizer-strip"),
            new SelectionOption<string>("Stage Banner", "stage-banner")
        ]);
        SelectComboValue(cmbPreset, settings.ObsOverlayPreset);

        chkDesignerProfile = CreateSwitch(palette, settings.ObsOverlayUseDesignerProfile);
        chkNowPlaying = CreateSwitch(palette, settings.ObsOverlayShowNowPlaying);
        chkLyrics = CreateSwitch(palette, settings.ObsOverlayShowLyrics);
        chkVisualizer = CreateSwitch(palette, settings.ObsOverlayShowVisualizer);
        chkQueue = CreateSwitch(palette, settings.ObsOverlayShowQueue);
        chkProgress = CreateSwitch(palette, settings.ObsOverlayShowProgress);
        chkNextLyric = CreateSwitch(palette, settings.ObsOverlayShowNextLyric);

        cmbArtworkShape = new ModernComboBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, Size = new Size(260, 36) };
        ThemeControlStyler.ApplyComboBoxTheme(cmbArtworkShape, palette);
        cmbArtworkShape.Items.AddRange([
            new SelectionOption<string>("Rounded", "rounded"),
            new SelectionOption<string>("Square", "square"),
            new SelectionOption<string>("Circle", "circle")
        ]);
        SelectComboValue(cmbArtworkShape, settings.ObsOverlayArtworkShape);

        sldOpacity = CreateSlider(settings.ObsOverlayBackgroundOpacity);
        sldScale = CreateSlider(settings.ObsOverlayScale, 75, 150);
        sldRadius = CreateSlider(settings.ObsOverlayCornerRadius, 0, 28);
        sldVisualizerIntensity = CreateSlider(settings.ObsOverlayVisualizerIntensity, 50, 200);
        ApplySliderTheme(sldOpacity, palette);
        ApplySliderTheme(sldScale, palette);
        ApplySliderTheme(sldRadius, palette);
        ApplySliderTheme(sldVisualizerIntensity, palette);

        lblOpacityValue = CreateValueLabel(palette);
        lblScaleValue = CreateValueLabel(palette);
        lblRadiusValue = CreateValueLabel(palette);
        lblVisualizerIntensityValue = CreateValueLabel(palette);
        UpdateDesignerValueLabels();

        preview = new PresetPreview(palette)
        {
            Margin = new Padding(0, 0, 0, 14),
            Size = new Size(640, 170)
        };

        lblStatus = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.25F),
            ForeColor = palette.TextMutedColor,
            Text = GetStatusText()
        };
        chkEnabled.CheckedChanged += (_, _) =>
        {
            settings.EnableObsOverlay = chkEnabled.Checked;
            UpdateUrlDisplay();
            lblStatus.Text = GetStatusText();
        };

        var btnClose = new ModernButton { Size = new Size(128, 40), Text = "Save & Close" };
        ThemeControlStyler.ApplyPrimaryButtonTheme(btnClose, palette, palette.AccentPrimaryColor);
        btnClose.Click += (_, _) =>
        {
            settings.EnableObsOverlay = chkEnabled.Checked;
            if (GetSelectedPreset() is { } preset)
                settings.ObsOverlayPreset = preset;
            ApplyDesignerControlsToSettings();
            DialogResult = DialogResult.OK;
            Close();
        };

        BuildLayout(palette, btnClose);
        WireDesignerControlEvents();
        cmbPreset.SelectedIndexChanged += (_, _) =>
        {
            if (GetSelectedPreset() is { } preset)
                settings.ObsOverlayPreset = preset;
            UpdateUrlDisplay();
        };
        UpdateUrlDisplay();
    }

    private void BuildLayout(ThemePalette palette, ModernButton btnClose)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = new Padding(28, 24, 28, 22)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle());
        root.BackColor = palette.WindowBackColor;

        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoSize = false, AutoScroll = true
        };
        body.BackColor = palette.WindowBackColor;

        body.Controls.Add(SectionLabel("OBS Overlay", palette));
        body.Controls.Add(SubLabel("Add your Spectralis overlay as an OBS browser source using the URL below. The token keeps the feed private.", palette, 640));
        body.Controls.Add(Row("Enable OBS overlay", chkEnabled, palette));
        urlRow = UrlRow(palette);
        body.Controls.Add(urlRow);
        body.Controls.Add(Row("Regenerate token", btnRegenToken, palette));
        body.Controls.Add(SectionLabel("Preset", palette));
        body.Controls.Add(Row("Layout preset", cmbPreset, palette));
        body.Controls.Add(preview);
        body.Controls.Add(SubLabel("Choose a preset to generate a preset-specific browser source URL.", palette, 640));
        body.Controls.Add(SectionLabel("Designer", palette));
        body.Controls.Add(SubLabel("Create a profile URL for this overlay source. Existing OBS URLs without these parameters keep using the preset defaults.", palette, 640));
        body.Controls.Add(Row("Use designer profile", chkDesignerProfile, palette));
        body.Controls.Add(Row("Now playing", chkNowPlaying, palette));
        body.Controls.Add(Row("Lyrics", chkLyrics, palette));
        body.Controls.Add(Row("Next lyric", chkNextLyric, palette));
        body.Controls.Add(Row("Visualizer", chkVisualizer, palette));
        body.Controls.Add(Row("Queue", chkQueue, palette));
        body.Controls.Add(Row("Progress bar", chkProgress, palette));
        body.Controls.Add(Row("Artwork shape", cmbArtworkShape, palette));
        body.Controls.Add(Row("Background opacity", CreateSliderHost(sldOpacity, lblOpacityValue), palette));
        body.Controls.Add(Row("Scale", CreateSliderHost(sldScale, lblScaleValue), palette));
        body.Controls.Add(Row("Corner radius", CreateSliderHost(sldRadius, lblRadiusValue), palette));
        body.Controls.Add(Row("Visualizer intensity", CreateSliderHost(sldVisualizerIntensity, lblVisualizerIntensityValue), palette));
        body.Controls.Add(lblStatus);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        footer.BackColor = palette.WindowBackColor;
        footer.Controls.Add(btnClose);

        root.Controls.Add(body, 0, 0);
        root.Controls.Add(footer, 0, 1);
        Controls.Add(root);
    }

    private Control UrlRow(ThemePalette palette)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = false,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Height = 40,
            Margin = new Padding(0, 0, 0, 14),
            Padding = Padding.Empty,
            RowCount = 1,
            Width = 640
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        row.BackColor = palette.WindowBackColor;
        txtUrl.Dock = DockStyle.Fill;
        row.Controls.Add(txtUrl, 0, 0);
        row.Controls.Add(btnCopyUrl, 2, 0);
        return row;
    }

    private static Label SectionLabel(string text, ThemePalette palette) =>
        new()
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = palette.TextPrimaryColor,
            Margin = new Padding(0, 0, 0, 4),
            Text = text
        };

    private static Label SubLabel(string text, ThemePalette palette, int maxWidth = 560) =>
        new()
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = palette.TextMutedColor,
            Margin = new Padding(0, 0, 0, 14),
            MaximumSize = new Size(maxWidth, 0),
            Text = text
        };

    private static Control Row(string label, Control control, ThemePalette palette)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0, 0, 0, 10),
            MinimumSize = new Size(640, 0), MaximumSize = new Size(640, 0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280F));
        row.BackColor = palette.WindowBackColor;
        var lbl = new Label
        {
            AutoSize = true, Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.25F),
            ForeColor = palette.TextPrimaryColor,
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft
        };
        row.Controls.Add(lbl, 0, 0);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = Padding.Empty;
        row.Controls.Add(control, 1, 0);
        return row;
    }

    private static ModernSwitch CreateSwitch(ThemePalette palette, bool isChecked)
    {
        var control = new ModernSwitch { Anchor = AnchorStyles.Left, Checked = isChecked };
        ThemeControlStyler.ApplySwitchTheme(control, palette);
        return control;
    }

    private static ModernSlider CreateSlider(int value, int minimum = 0, int maximum = 100) =>
        new()
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Minimum = minimum,
            Maximum = maximum,
            Size = new Size(188, 32),
            Value = value
        };

    private static void ApplySliderTheme(ModernSlider slider, ThemePalette palette)
    {
        slider.TrackColor = palette.SurfaceAltBackColor;
        slider.AccentStartColor = palette.AccentPrimaryColor;
        slider.AccentEndColor = palette.AccentSecondaryColor;
        slider.FocusColor = palette.AccentPrimaryColor;
    }

    private static Label CreateValueLabel(ThemePalette palette) =>
        new()
        {
            AutoSize = false,
            Font = new Font("Segoe UI", 8.25F),
            ForeColor = palette.TextMutedColor,
            Margin = new Padding(8, 0, 0, 0),
            Size = new Size(54, 32),
            TextAlign = ContentAlignment.MiddleRight
        };

    private static Control CreateSliderHost(ModernSlider slider, Label valueLabel)
    {
        var host = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Height = 34,
            Margin = Padding.Empty,
            RowCount = 1
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        slider.Dock = DockStyle.Fill;
        valueLabel.Dock = DockStyle.Fill;
        host.Controls.Add(slider, 0, 0);
        host.Controls.Add(valueLabel, 1, 0);
        return host;
    }

    private void WireDesignerControlEvents()
    {
        chkDesignerProfile.CheckedChanged += (_, _) =>
        {
            ApplyDesignerControlsToSettings();
            UpdateDesignerControlsEnabled();
            UpdateUrlDisplay();
        };

        foreach (var sw in new[]
        {
            chkNowPlaying,
            chkLyrics,
            chkVisualizer,
            chkQueue,
            chkProgress,
            chkNextLyric
        })
        {
            sw.CheckedChanged += (_, _) =>
            {
                ApplyDesignerControlsToSettings();
                UpdateUrlDisplay();
            };
        }

        cmbArtworkShape.SelectedIndexChanged += (_, _) =>
        {
            ApplyDesignerControlsToSettings();
            UpdateUrlDisplay();
        };

        foreach (var slider in new[]
        {
            sldOpacity,
            sldScale,
            sldRadius,
            sldVisualizerIntensity
        })
        {
            slider.Scroll += (_, _) =>
            {
                ApplyDesignerControlsToSettings();
                UpdateDesignerValueLabels();
                UpdateUrlDisplay();
            };
        }

        UpdateDesignerControlsEnabled();
    }

    private void UpdateDesignerControlsEnabled()
    {
        var enabled = chkDesignerProfile.Checked;
        foreach (var control in new Control[]
        {
            chkNowPlaying,
            chkLyrics,
            chkVisualizer,
            chkQueue,
            chkProgress,
            chkNextLyric,
            cmbArtworkShape,
            sldOpacity,
            sldScale,
            sldRadius,
            sldVisualizerIntensity
        })
        {
            control.Enabled = enabled;
        }
    }

    private void ApplyDesignerControlsToSettings()
    {
        settings.ObsOverlayUseDesignerProfile = chkDesignerProfile.Checked;
        settings.ObsOverlayShowNowPlaying = chkNowPlaying.Checked;
        settings.ObsOverlayShowLyrics = chkLyrics.Checked;
        settings.ObsOverlayShowVisualizer = chkVisualizer.Checked;
        settings.ObsOverlayShowQueue = chkQueue.Checked;
        settings.ObsOverlayShowProgress = chkProgress.Checked;
        settings.ObsOverlayShowNextLyric = chkNextLyric.Checked;
        settings.ObsOverlayBackgroundOpacity = sldOpacity.Value;
        settings.ObsOverlayScale = sldScale.Value;
        settings.ObsOverlayCornerRadius = sldRadius.Value;
        settings.ObsOverlayVisualizerIntensity = sldVisualizerIntensity.Value;
        settings.ObsOverlayArtworkShape = GetSelectedValue(cmbArtworkShape, "rounded");
    }

    private void UpdateDesignerValueLabels()
    {
        lblOpacityValue.Text = $"{sldOpacity.Value}%";
        lblScaleValue.Text = $"{sldScale.Value}%";
        lblRadiusValue.Text = $"{sldRadius.Value}px";
        lblVisualizerIntensityValue.Text = $"{sldVisualizerIntensity.Value}%";
    }

    private void CopyUrl()
    {
        if (GetSelectedPreset() is not { } preset)
        {
            lblStatus.Text = "Choose a layout preset before copying the URL.";
            return;
        }

        var url = BuildCurrentUrl(preset);
        Clipboard.SetText(url);
        lblStatus.Text = "URL copied to clipboard.";
    }

    private void RegenToken()
    {
        if (MessageBox.Show(this,
            "Regenerating the token will invalidate the current overlay URL. OBS browser sources will need to be updated. Continue?",
            "Regenerate Token", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        settings.ObsOverlayToken = Guid.NewGuid().ToString("N");
        UpdateUrlDisplay();
        lblStatus.Text = "Token regenerated. Restart OBS overlay to apply.";
    }

    private void UpdateUrlDisplay()
    {
        var preset = GetSelectedPreset();
        var hasPreset = preset is not null;
        if (urlRow is not null)
            urlRow.Visible = hasPreset;
        txtUrl.Visible = hasPreset;
        btnCopyUrl.Visible = hasPreset;
        preview.Preset = preset;

        if (preset is null)
            return;

        txtUrl.Text = BuildCurrentUrl(preset);
    }

    private string BuildCurrentUrl(string preset)
    {
        var url = server?.BaseUrl ?? BuildPreviewUrl(settings);
        var query = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrEmpty(preset) && preset != "compact")
            query.Add(new KeyValuePair<string, string>("preset", preset));

        if (settings.ObsOverlayUseDesignerProfile)
        {
            var sections = BuildDesignerSections();
            query.Add(new KeyValuePair<string, string>("sections", string.Join(',', sections)));
            query.Add(new KeyValuePair<string, string>("progress", settings.ObsOverlayShowProgress ? "1" : "0"));
            query.Add(new KeyValuePair<string, string>("nextLyric", settings.ObsOverlayShowNextLyric ? "1" : "0"));
            query.Add(new KeyValuePair<string, string>("opacity", settings.ObsOverlayBackgroundOpacity.ToString()));
            query.Add(new KeyValuePair<string, string>("scale", settings.ObsOverlayScale.ToString()));
            query.Add(new KeyValuePair<string, string>("radius", settings.ObsOverlayCornerRadius.ToString()));
            query.Add(new KeyValuePair<string, string>("viz", settings.ObsOverlayVisualizerIntensity.ToString()));
            query.Add(new KeyValuePair<string, string>("art", settings.ObsOverlayArtworkShape));
        }

        return query.Count == 0 ? url : $"{url}?{BuildQuery(query)}";
    }

    private string[] BuildDesignerSections()
    {
        var sections = new List<string>();
        if (settings.ObsOverlayShowNowPlaying)
            sections.Add("nowplaying");
        if (settings.ObsOverlayShowLyrics)
            sections.Add("lyrics");
        if (settings.ObsOverlayShowVisualizer)
            sections.Add("viz");
        if (settings.ObsOverlayShowQueue)
            sections.Add("queue");
        return [..sections];
    }

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join("&", values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private string? GetSelectedPreset() =>
        cmbPreset.SelectedItem is SelectionOption<string> option
            ? AppSettingsStore.NormalizeObsOverlayPreset(option.Value)
            : null;

    private static string GetSelectedValue(ModernComboBox comboBox, string fallback) =>
        comboBox.SelectedItem is SelectionOption<string> option ? option.Value : fallback;

    private static void SelectComboValue(ModernComboBox comboBox, string value)
    {
        for (var index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is SelectionOption<string> option &&
                string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex = index;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;
    }

    private string GetStatusText() =>
        settings.EnableObsOverlay
            ? $"Server running on port {settings.ObsOverlayPort}"
            : "OBS overlay is disabled. Enable it to start the server.";

    private static string BuildPreviewUrl(AppSettings s) =>
        $"http://127.0.0.1:{s.ObsOverlayPort}/obs/{s.ObsOverlayToken}";

    private sealed class PresetPreview : Control
    {
        private readonly ThemePalette palette;
        private string? preset;

        public PresetPreview(ThemePalette palette)
        {
            this.palette = palette;
            DoubleBuffered = true;
            BackColor = palette.WindowBackColor;
            ForeColor = palette.TextMutedColor;
        }

        public string? Preset
        {
            get => preset;
            set
            {
                if (string.Equals(preset, value, StringComparison.Ordinal))
                    return;
                preset = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using var surface = new SolidBrush(palette.SurfaceBackColor);
            using var border = new Pen(palette.BorderColor);
            using var muted = new SolidBrush(palette.TextMutedColor);
            using var text = new SolidBrush(palette.TextPrimaryColor);
            using var accent = new SolidBrush(palette.AccentPrimaryColor);
            using var dark = new SolidBrush(Color.FromArgb(190, 10, 14, 22));
            using var faint = new SolidBrush(Color.FromArgb(45, palette.AccentPrimaryColor));

            FillRoundRect(g, surface, bounds, 8);
            DrawRoundRect(g, border, bounds, 8);

            if (preset is null)
            {
                TextRenderer.DrawText(g, "Select a preset to preview the OBS layout.", Font, bounds, palette.TextMutedColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                return;
            }

            var frame = new Rectangle(18, 18, Width - 37, Height - 37);
            using var framePen = new Pen(Color.FromArgb(70, palette.TextMutedColor));
            g.DrawRectangle(framePen, frame);
            DrawPreset(g, frame, dark, faint, accent, text, muted);
        }

        private void DrawPreset(Graphics g, Rectangle frame, Brush dark, Brush faint, Brush accent, Brush text, Brush muted)
        {
            switch (preset)
            {
                case "lyrics-lower-third":
                    Bar(g, new Rectangle(frame.Left, frame.Bottom - 44, frame.Width, 44), faint);
                    CenterLines(g, frame, frame.Bottom - 34, accent, text);
                    break;
                case "full-visualizer":
                    DrawBars(g, new Rectangle(frame.Left + 18, frame.Bottom - 56, frame.Width - 36, 38), accent);
                    Card(g, new Rectangle(frame.Left + 16, frame.Top + 14, 150, 38), dark, text, muted);
                    break;
                case "queue-sidebar":
                    Card(g, new Rectangle(frame.Right - 170, frame.Top + 12, 150, frame.Height - 24), dark, text, muted);
                    DrawList(g, new Rectangle(frame.Right - 154, frame.Top + 72, 120, 58), muted);
                    break;
                case "vertical-stream":
                    Card(g, new Rectangle(frame.Right - 118, frame.Top, 118, frame.Height), dark, text, muted);
                    Album(g, new Rectangle(frame.Right - 89, frame.Top + 18, 60, 60), accent);
                    break;
                case "capsule-mode":
                    Album(g, new Rectangle(frame.Left + frame.Width / 2 - 34, frame.Top + 24, 68, 68), accent);
                    CenterLines(g, frame, frame.Top + 108, accent, text);
                    break;
                case "minimal-ticker":
                    Card(g, new Rectangle(frame.Left + 28, frame.Bottom - 38, frame.Width - 56, 24), dark, text, muted);
                    break;
                case "album-card":
                    Card(g, new Rectangle(frame.Left + 24, frame.Top + 18, 178, 104), dark, text, muted);
                    Album(g, new Rectangle(frame.Left + 38, frame.Top + 34, 58, 58), accent);
                    break;
                case "lyrics-focus":
                    CenterLines(g, frame, frame.Top + 48, accent, text);
                    g.FillRectangle(faint, frame.Left, frame.Bottom - 26, frame.Width, 26);
                    break;
                case "visualizer-strip":
                    DrawBars(g, new Rectangle(frame.Left, frame.Bottom - 34, frame.Width, 30), accent);
                    Card(g, new Rectangle(frame.Left + 16, frame.Top + 14, 170, 36), dark, text, muted);
                    break;
                case "stage-banner":
                    Card(g, new Rectangle(frame.Left + 40, frame.Top + 22, frame.Width - 80, 76), dark, text, muted);
                    DrawBars(g, new Rectangle(frame.Left + 58, frame.Top + 106, frame.Width - 116, 22), accent);
                    break;
                default:
                    Card(g, new Rectangle(frame.Left + 20, frame.Bottom - 64, 210, 44), dark, text, muted);
                    DrawBars(g, new Rectangle(frame.Left + 34, frame.Bottom - 16, 180, 8), accent);
                    break;
            }
        }

        private static void Card(Graphics g, Rectangle r, Brush fill, Brush text, Brush muted)
        {
            FillRoundRect(g, fill, r, 7);
            Album(g, new Rectangle(r.Left + 10, r.Top + 10, Math.Min(42, r.Height - 20), Math.Min(42, r.Height - 20)), text);
            g.FillRectangle(text, r.Left + 62, r.Top + 13, Math.Max(22, r.Width - 86), 7);
            g.FillRectangle(muted, r.Left + 62, r.Top + 28, Math.Max(18, r.Width - 110), 5);
        }

        private static void Album(Graphics g, Rectangle r, Brush fill) => FillRoundRect(g, fill, r, 5);

        private static void Bar(Graphics g, Rectangle r, Brush fill) => g.FillRectangle(fill, r);

        private static void CenterLines(Graphics g, Rectangle frame, int y, Brush accent, Brush text)
        {
            g.FillRectangle(text, frame.Left + frame.Width / 2 - 150, y, 300, 8);
            g.FillRectangle(accent, frame.Left + frame.Width / 2 - 96, y + 18, 192, 6);
        }

        private static void DrawList(Graphics g, Rectangle r, Brush fill)
        {
            for (var i = 0; i < 4; i++)
                g.FillRectangle(fill, r.Left, r.Top + i * 15, r.Width - i * 14, 5);
        }

        private static void DrawBars(Graphics g, Rectangle r, Brush fill)
        {
            var bars = 24;
            var gap = 3;
            var bw = Math.Max(2, (r.Width - gap * (bars - 1)) / bars);
            for (var i = 0; i < bars; i++)
            {
                var wave = Math.Abs(Math.Sin(i * 0.55));
                var h = Math.Max(4, (int)(r.Height * (0.28 + wave * 0.72)));
                g.FillRectangle(fill, r.Left + i * (bw + gap), r.Bottom - h, bw, h);
            }
        }

        private static void FillRoundRect(Graphics g, Brush brush, Rectangle r, int radius)
        {
            using var path = RoundedPath(r, radius);
            g.FillPath(brush, path);
        }

        private static void DrawRoundRect(Graphics g, Pen pen, Rectangle r, int radius)
        {
            using var path = RoundedPath(r, radius);
            g.DrawPath(pen, path);
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            var d = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
