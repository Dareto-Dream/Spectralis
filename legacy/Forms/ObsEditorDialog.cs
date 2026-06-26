using System.Drawing;

namespace Spectralis;

/// <summary>
/// Full OBS layout editor with drag-and-drop canvas, widget palette, and properties panel.
/// </summary>
internal sealed class ObsEditorDialog : Form
{
    private readonly AppSettings settings;
    private readonly ObsOverlayServer? server;
    private readonly IReadOnlyList<InstalledVisualizerDefinition> installedVisualizers;
    private readonly ThemePalette palette;

    // Canvas
    private readonly ObsEditorCanvas canvas;

    // Palette
    private readonly Panel palettePanel;

    // Properties panel
    private readonly Panel propsPanel;
    private readonly Label lblPropsTitle;
    private Panel? propsContent;

    // Footer
    private readonly TextBox txtUrl;
    private readonly ModernSwitch chkEnabled;
    private readonly Label lblStatus;

    // Preset bar
    private FlowLayoutPanel? _presetChipFlow;

    private static readonly string[] BuiltInVizNames =
    [
        "Mirror Spectrum",
        "Spectrum",
        "Oscilloscope",
        "Waveform",
        "Spectrum Wave",
        "VU Meter",
        "Radial Spectrum",
        "Spinning Disk",
        "Album Cover",
        "Dancing Colors",
        "Sphere 3D",
        "Graph 3D",
        // Minimeter-style
        "LED Meter",
        "Vectorscope",
        "Spectrogram",
        "Bounce Bars",
        "Circular EQ",
        "Block Grid",
    ];

    private static readonly VisualizerMode[] BuiltInVizModes =
    [
        VisualizerMode.MirrorSpectrum,
        VisualizerMode.Spectrum,
        VisualizerMode.Oscilloscope,
        VisualizerMode.Waveform,
        VisualizerMode.SpectrumWave,
        VisualizerMode.VUMeter,
        VisualizerMode.RadialSpectrum,
        VisualizerMode.SpinningDisk,
        VisualizerMode.AlbumCover,
        VisualizerMode.DancingColors,
        VisualizerMode.Sphere3D,
        VisualizerMode.Graph3D,
        // Minimeter-style
        VisualizerMode.LedMeter,
        VisualizerMode.Vectorscope,
        VisualizerMode.Spectrogram,
        VisualizerMode.BounceBars,
        VisualizerMode.CircularEq,
        VisualizerMode.BlockGrid,
    ];

    public ObsEditorDialog(
        AppSettings settings,
        ObsOverlayServer? server,
        IReadOnlyList<InstalledVisualizerDefinition> installedVisualizers)
    {
        this.settings = settings;
        this.server = server;
        this.installedVisualizers = installedVisualizers;
        palette = ThemePalette.Create(settings.ThemeMode, settings.ThemeAccent);

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1260, 740);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(960, 580);
        MaximizeBox = true;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "OBS Layout Editor";
        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;
        WindowChromeStyler.ApplyTheme(this, palette);

        // ── Canvas ────────────────────────────────────────────────────────
        canvas = new ObsEditorCanvas(palette)
        {
            Dock = DockStyle.Fill
        };
        canvas.SelectionChanged += (_, _) => RefreshPropertiesPanel();
        canvas.LayoutChanged    += (_, _) => UpdateUrlDisplay();

        // ── Palette (left) ────────────────────────────────────────────────
        palettePanel = BuildPalettePanel();

        // ── Properties (right) ───────────────────────────────────────────
        propsPanel = new Panel
        {
            Width = 250,
            Dock = DockStyle.Right,
            BackColor = palette.SurfaceBackColor,
            Padding = new Padding(12)
        };
        lblPropsTitle = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 28,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
            ForeColor = palette.TextPrimaryColor,
            Text = "Properties",
            TextAlign = ContentAlignment.MiddleLeft
        };
        propsPanel.Controls.Add(lblPropsTitle);

        // Splitters
        var splitterRight = new Splitter { Dock = DockStyle.Right, Width = 4, BackColor = palette.BorderColor };
        var splitterLeft  = new Splitter { Dock = DockStyle.Left,  Width = 4, BackColor = palette.BorderColor };

        // ── Footer ─────────────────────────────────────────────────────────
        var footer = BuildFooter(out chkEnabled, out txtUrl, out lblStatus);

        // ── Canvas wrapper ──────────────────────────────────────────────────
        var canvasWrapper = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 14, 18) };
        canvasWrapper.Controls.Add(canvas);
        canvasWrapper.Controls.Add(BuildToolbar());
        canvasWrapper.Controls.Add(BuildPresetBar());

        Controls.Add(canvasWrapper);
        Controls.Add(splitterRight);
        Controls.Add(propsPanel);
        Controls.Add(splitterLeft);
        Controls.Add(palettePanel);
        Controls.Add(footer);

        // Load saved layout
        var layout = ObsLayout.FromJson(settings.ObsOverlayLayout) ?? ObsLayout.CreateDefault();
        canvas.SetWidgets(layout.Widgets);

        UpdateUrlDisplay();
        RefreshPropertiesPanel();
    }

    // ── Palette builder ───────────────────────────────────────────────────

    private Panel BuildPalettePanel()
    {
        var panel = new Panel
        {
            Width = 200,
            Dock = DockStyle.Left,
            BackColor = palette.SurfaceBackColor,
            Padding = new Padding(10, 12, 10, 10),
            AutoScroll = true
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = false,
            AutoScroll = false
        };
        flow.BackColor = palette.SurfaceBackColor;

        void AddSection(string title)
        {
            var lbl = new Label
            {
                Text = title,
                AutoSize = false,
                Height = 22,
                Width = 176,
                Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
                ForeColor = palette.TextMutedColor,
                Margin = new Padding(0, 10, 0, 2)
            };
            flow.Controls.Add(lbl);
        }

        void AddWidgetButton(string label, Func<ObsLayoutWidget> factory)
        {
            var btn = new ModernButton
            {
                Text = label,
                Width = 176,
                Height = 34,
                Margin = new Padding(0, 2, 0, 0)
            };
            ThemeControlStyler.ApplyGhostButtonTheme(btn, palette, palette.AccentPrimaryColor);
            btn.Click += (_, _) => canvas.AddWidget(factory());
            flow.Controls.Add(btn);
        }

        AddSection("CONTENT");
        AddWidgetButton("+ Now Playing",  () => new ObsLayoutWidget { Type = ObsWidgetType.NowPlaying,  X = 0.02, Y = 0.78, W = 0.30, H = 0.13 });
        AddWidgetButton("+ Lyrics",       () => new ObsLayoutWidget { Type = ObsWidgetType.Lyrics,      X = 0.25, Y = 0.85, W = 0.50, H = 0.10, BgOpacity = 0 });
        AddWidgetButton("+ Queue",        () => new ObsLayoutWidget { Type = ObsWidgetType.Queue,       X = 0.72, Y = 0.04, W = 0.26, H = 0.35 });
        AddWidgetButton("+ Progress Bar", () => new ObsLayoutWidget { Type = ObsWidgetType.Progress,   X = 0.00, Y = 0.97, W = 1.00, H = 0.03, BgOpacity = 0 });
        AddWidgetButton("+ Song Wars Bracket", () => new ObsLayoutWidget { Type = ObsWidgetType.SongWarsBracket, X = 0.03, Y = 0.05, W = 0.94, H = 0.72, BgOpacity = 82, Radius = 8 });

        AddSection("VISUALIZERS");
        for (var i = 0; i < BuiltInVizNames.Length; i++)
        {
            var mode = BuiltInVizModes[i];
            var name = BuiltInVizNames[i];
            AddWidgetButton($"+ {name}", () => new ObsLayoutWidget
            {
                Type = ObsWidgetType.Visualizer,
                VizKey = VisualizerChoice.BuiltIn(mode).Key,
                X = 0.02, Y = 0.68, W = 0.30, H = 0.09,
                BgOpacity = 0
            });
        }

        if (installedVisualizers.Count > 0)
        {
            AddSection("CUSTOM VISUALIZERS");
            foreach (var viz in installedVisualizers)
            {
                var hasBanner = HasObsBanner(viz);
                var id = viz.Id;
                var name = viz.DisplayName;
                var label = hasBanner ? $"+ {name}" : $"+ {name}  ⚠";

                var btn = new ModernButton
                {
                    Text = label,
                    Width = 176,
                    Height = 34,
                    Margin = new Padding(0, 2, 0, 0)
                };
                ThemeControlStyler.ApplyGhostButtonTheme(btn, palette,
                    hasBanner ? palette.AccentPrimaryColor : palette.DangerColor);
                if (!hasBanner)
                {
                    var tip = new ToolTip();
                    tip.SetToolTip(btn, "No obs_banner asset defined. Will only appear if 'Allow missing banners' is enabled in settings.");
                }
                btn.Click += (_, _) =>
                {
                    if (!hasBanner && !settings.ObsOverlayAllowMissingCustomBanner)
                    {
                        MessageBox.Show(this,
                            $"'{name}' has no obs_banner defined.\n\nEnable 'Allow missing custom banners' in settings to add it anyway (shows generic spectrum bars).",
                            "No OBS Banner",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    canvas.AddWidget(new ObsLayoutWidget
                    {
                        Type = ObsWidgetType.Visualizer,
                        VizKey = VisualizerChoice.Installed(id).Key,
                        X = 0.02, Y = 0.68, W = 0.30, H = 0.14,
                        BgOpacity = 0
                    });
                };
                flow.Controls.Add(btn);
            }
        }

        panel.Controls.Add(flow);
        return panel;
    }

    private bool HasObsBanner(InstalledVisualizerDefinition viz) =>
        viz.HtmlContext?.BinaryAssets.ContainsKey("obs_banner.html") == true ||
        viz.HtmlContext?.BinaryAssets.ContainsKey("obs_banner") == true ||
        viz.HtmlContext?.TextAssets.ContainsKey("obs_banner.html") == true;

    // ── Toolbar (snap + canvas controls) ─────────────────────────────────

    private Panel BuildToolbar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = Color.FromArgb(20, 20, 26),
            Padding = new Padding(8, 5, 8, 5)
        };
        var border = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = palette.BorderColor };

        // Snap toggle
        var snapSwitch = new ModernSwitch { Width = 46, Top = 6 };
        ThemeControlStyler.ApplySwitchTheme(snapSwitch, palette);
        var snapTip = new ToolTip();
        snapTip.SetToolTip(snapSwitch, "Snap widgets to a 2.5% grid while dragging");
        snapSwitch.CheckedChanged += (_, _) => canvas.SnapToGrid = snapSwitch.Checked;

        var lblSnap = new Label
        {
            Text = "Snap to grid",
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = palette.TextMutedColor
        };

        bar.Controls.Add(border);
        bar.Controls.Add(snapSwitch);
        bar.Controls.Add(lblSnap);

        bar.Layout += (_, _) =>
        {
            var midY = bar.Height / 2;
            snapSwitch.Left = 10;
            snapSwitch.Top  = midY - snapSwitch.Height / 2;
            lblSnap.Left    = snapSwitch.Right + 7;
            lblSnap.Top     = midY - lblSnap.Height / 2;
        };

        return bar;
    }

    // ── Footer builder ────────────────────────────────────────────────────

    private Panel BuildFooter(
        out ModernSwitch chkEnabledOut,
        out TextBox txtUrlOut,
        out Label lblStatusOut)
    {
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = palette.SurfaceBackColor,
            Padding = new Padding(14, 8, 14, 8)
        };
        var borderPanel = new Panel
        {
            Dock = DockStyle.Top, Height = 1, BackColor = palette.BorderColor
        };
        footer.Controls.Add(borderPanel);

        var chkEnabled = new ModernSwitch { Width = 60, Checked = settings.EnableObsOverlay };
        chkEnabled.Top = (footer.Height - chkEnabled.Height) / 2;
        chkEnabled.Left = 14;
        ThemeControlStyler.ApplySwitchTheme(chkEnabled, palette);
        chkEnabled.CheckedChanged += (_, _) =>
        {
            settings.EnableObsOverlay = chkEnabled.Checked;
        };

        var lblEnable = new Label
        {
            Text = "Enable",
            AutoSize = true,
            Font = new Font("Segoe UI", 9F),
            ForeColor = palette.TextPrimaryColor
        };
        lblEnable.Top = chkEnabled.Top + (chkEnabled.Height - lblEnable.PreferredHeight) / 2;
        lblEnable.Left = chkEnabled.Right + 8;

        var txtUrl = new TextBox
        {
            Font = new Font("Consolas", 8.5F),
            ReadOnly = true,
            Height = 28,
            BackColor = palette.SurfaceAltBackColor,
            ForeColor = palette.TextPrimaryColor
        };
        txtUrl.Top = (footer.Height - txtUrl.Height) / 2;

        var btnCopy = new ModernButton { Width = 80, Height = 32, Text = "Copy URL" };
        btnCopy.Top = (footer.Height - btnCopy.Height) / 2;
        ThemeControlStyler.ApplyPrimaryButtonTheme(btnCopy, palette, palette.AccentPrimaryColor);
        btnCopy.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(txtUrl.Text))
                Clipboard.SetText(txtUrl.Text);
        };

        var btnSave = new ModernButton { Width = 110, Height = 32, Text = "Save & Close" };
        btnSave.Top = (footer.Height - btnSave.Height) / 2;
        ThemeControlStyler.ApplyPrimaryButtonTheme(btnSave, palette, palette.AccentPrimaryColor);
        btnSave.Click += (_, _) =>
        {
            SaveLayout();
            settings.EnableObsOverlay = chkEnabled.Checked;
            DialogResult = DialogResult.OK;
            Close();
        };

        var lblStatus = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8F),
            ForeColor = palette.TextMutedColor,
            Text = GetStatusText()
        };
        lblStatus.Top = (footer.Height - lblStatus.PreferredHeight) / 2;

        var chkAllowMissing = new ModernSwitch
        {
            Width = 60,
            Checked = settings.ObsOverlayAllowMissingCustomBanner
        };
        chkAllowMissing.Top = (footer.Height - chkAllowMissing.Height) / 2;
        ThemeControlStyler.ApplySwitchTheme(chkAllowMissing, palette);
        var tip2 = new ToolTip();
        tip2.SetToolTip(chkAllowMissing, "Allow custom visualizers without an obs_banner asset (shows generic bars).");
        chkAllowMissing.CheckedChanged += (_, _) =>
        {
            settings.ObsOverlayAllowMissingCustomBanner = chkAllowMissing.Checked;
        };
        var lblAllowMissing = new Label
        {
            Text = "Allow missing banners",
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = palette.TextMutedColor
        };
        lblAllowMissing.Top = chkAllowMissing.Top + (chkAllowMissing.Height - lblAllowMissing.PreferredHeight) / 2;

        chkEnabled.CheckedChanged += (_, _) => lblStatus.Text = GetStatusText();

        footer.Controls.AddRange([chkEnabled, lblEnable, txtUrl, btnCopy, btnSave, lblStatus, chkAllowMissing, lblAllowMissing]);
        footer.Resize += (_, _) => LayoutFooterControls(footer, chkEnabled, lblEnable, chkAllowMissing, lblAllowMissing, txtUrl, btnCopy, btnSave, lblStatus);
        LayoutFooterControls(footer, chkEnabled, lblEnable, chkAllowMissing, lblAllowMissing, txtUrl, btnCopy, btnSave, lblStatus);

        chkEnabledOut = chkEnabled;
        txtUrlOut = txtUrl;
        lblStatusOut = lblStatus;
        return footer;
    }

    private static void LayoutFooterControls(
        Panel footer,
        Control chkEnabled, Control lblEnable,
        Control chkMissing, Control lblMissing,
        Control txtUrl, Control btnCopy, Control btnSave, Control lblStatus)
    {
        var h = footer.Height;
        var midY = h / 2;

        chkEnabled.Top = midY - chkEnabled.Height / 2;
        chkEnabled.Left = 14;
        lblEnable.Top = midY - lblEnable.Height / 2;
        lblEnable.Left = chkEnabled.Right + 6;

        btnSave.Top = midY - btnSave.Height / 2;
        btnSave.Left = footer.Width - btnSave.Width - 14;
        btnCopy.Top = midY - btnCopy.Height / 2;
        btnCopy.Left = btnSave.Left - btnCopy.Width - 8;

        chkMissing.Top = midY - chkMissing.Height / 2;
        chkMissing.Left = btnCopy.Left - 200;
        lblMissing.Top = midY - lblMissing.Height / 2;
        lblMissing.Left = chkMissing.Right + 6;

        var urlLeft  = lblEnable.Right + 14;
        var urlRight = chkMissing.Left - 14;
        txtUrl.Top   = midY - txtUrl.Height / 2;
        txtUrl.Left  = urlLeft;
        txtUrl.Width = Math.Max(60, urlRight - urlLeft);

        lblStatus.Top  = midY - lblStatus.Height / 2;
        lblStatus.Left = 14;
    }

    // ── Properties panel ─────────────────────────────────────────────────

    private void RefreshPropertiesPanel()
    {
        propsContent?.Dispose();
        propsContent = null;

        var w = canvas.SelectedWidget;
        if (w is null)
        {
            lblPropsTitle.Text = "Properties";
            return;
        }

        var pw = (int)Math.Round(w.W * 1920);
        var ph = (int)Math.Round(w.H * 1080);
        lblPropsTitle.Text = w.Type switch
        {
            ObsWidgetType.NowPlaying  => $"Now Playing  ({pw}×{ph}px)",
            ObsWidgetType.Lyrics      => $"Lyrics  ({pw}×{ph}px)",
            ObsWidgetType.Queue       => $"Queue  ({pw}×{ph}px)",
            ObsWidgetType.Progress    => $"Progress Bar  ({pw}×{ph}px)",
            ObsWidgetType.Visualizer  => $"Visualizer  ({pw}×{ph}px)",
            ObsWidgetType.SongWarsBracket => $"Song Wars Bracket  ({pw}×{ph}px)",
            _                         => "Properties"
        };

        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 4, 0, 0),
            BackColor = palette.SurfaceBackColor
        };

        void AddProp(string label, Control ctrl)
        {
            var row = new TableLayoutPanel
            {
                ColumnCount = 1, RowCount = 2,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10),
                Width = 224
            };
            row.BackColor = palette.SurfaceBackColor;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            var lbl = new Label
            {
                Text = label,
                AutoSize = true,
                Font = new Font("Segoe UI", 8F),
                ForeColor = palette.TextMutedColor,
                Margin = new Padding(0, 0, 0, 2)
            };
            ctrl.Width = 224;
            ctrl.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            row.Controls.Add(lbl, 0, 0);
            row.Controls.Add(ctrl, 0, 1);
            content.Controls.Add(row);
        }

        void AddSectionLabel(string text)
        {
            var lbl = new Label
            {
                Text = text,
                AutoSize = false,
                Height = 20,
                Width = 224,
                Font = new Font("Segoe UI Semibold", 7.5F, FontStyle.Bold),
                ForeColor = palette.TextMutedColor,
                Margin = new Padding(0, 8, 0, 2)
            };
            content.Controls.Add(lbl);
        }

        ModernSwitch MakeSwitch(bool val, Action<bool> onChange)
        {
            var sw = new ModernSwitch { Checked = val, Width = 60 };
            ThemeControlStyler.ApplySwitchTheme(sw, palette);
            sw.CheckedChanged += (_, _) => { onChange(sw.Checked); canvas.NotifySelectedChanged(); };
            return sw;
        }

        ModernSlider MakeSlider(int val, int min, int max, Action<int> onChange)
        {
            var sl = new ModernSlider { Minimum = min, Maximum = max, Value = val, Width = 224, Height = 28 };
            sl.TrackColor = palette.SurfaceAltBackColor;
            sl.AccentStartColor = palette.AccentPrimaryColor;
            sl.AccentEndColor = palette.AccentSecondaryColor;
            sl.FocusColor = palette.AccentPrimaryColor;
            sl.Scroll += (_, _) => { onChange(sl.Value); canvas.NotifySelectedChanged(); RefreshPropsTitle(); };
            return sl;
        }

        void RefreshPropsTitle()
        {
            var ww = (int)Math.Round(w.W * 1920);
            var wh = (int)Math.Round(w.H * 1080);
            lblPropsTitle.Text = w.Type switch
            {
                ObsWidgetType.NowPlaying  => $"Now Playing  ({ww}×{wh}px)",
                ObsWidgetType.Lyrics      => $"Lyrics  ({ww}×{wh}px)",
                ObsWidgetType.Queue       => $"Queue  ({ww}×{wh}px)",
                ObsWidgetType.Progress    => $"Progress Bar  ({ww}×{wh}px)",
                ObsWidgetType.Visualizer  => $"Visualizer  ({ww}×{wh}px)",
                ObsWidgetType.SongWarsBracket => $"Song Wars Bracket  ({ww}×{wh}px)",
                _                         => "Properties"
            };
        }

        void AddPosSizeSliders()
        {
            AddProp("X position", MakeSlider((int)Math.Round(w.X * 100), 0, 99, v => w.X = v / 100.0));
            AddProp("Y position", MakeSlider((int)Math.Round(w.Y * 100), 0, 99, v => w.Y = v / 100.0));
            AddProp("Width %",    MakeSlider((int)Math.Round(w.W * 100), 1, 100, v => w.W = v / 100.0));
            AddProp("Height %",   MakeSlider((int)Math.Round(w.H * 100), 1, 100, v => w.H = v / 100.0));
        }

        AddPosSizeSliders();

        switch (w.Type)
        {
            case ObsWidgetType.NowPlaying:
                AddProp("Background opacity", MakeSlider(w.BgOpacity, 0, 100, v => w.BgOpacity = v));
                AddProp("Corner radius",      MakeSlider(w.Radius, 0, 28, v => w.Radius = v));
                AddProp("Show artwork",       MakeSwitch(w.ShowArt,      v => w.ShowArt = v));
                AddProp("Show artist",        MakeSwitch(w.ShowArtist,   v => w.ShowArtist = v));
                AddProp("Show progress bar",  MakeSwitch(w.ShowProgress, v => w.ShowProgress = v));
                AddProp("Artwork shape",      MakeArtShapeCombo(w));
                break;

            case ObsWidgetType.Lyrics:
                AddProp("Background opacity", MakeSlider(w.BgOpacity, 0, 100, v => w.BgOpacity = v));
                AddProp("Corner radius",      MakeSlider(w.Radius, 0, 28, v => w.Radius = v));
                AddProp("Show next lyric",    MakeSwitch(w.ShowNext, v => w.ShowNext = v));
                break;

            case ObsWidgetType.Queue:
                AddProp("Background opacity", MakeSlider(w.BgOpacity, 0, 100, v => w.BgOpacity = v));
                AddProp("Corner radius",      MakeSlider(w.Radius, 0, 28, v => w.Radius = v));
                AddProp("Max items",          MakeSlider(w.MaxItems, 2, 15, v => w.MaxItems = v));
                break;

            case ObsWidgetType.Progress:
                AddProp("Background opacity", MakeSlider(w.BgOpacity, 0, 100, v => w.BgOpacity = v));
                AddProp("Corner radius",      MakeSlider(w.Radius, 0, 10, v => w.Radius = v));
                break;

            case ObsWidgetType.Visualizer:
                AddProp("Visualizer",         MakeVizKeyCombo(w));
                AddProp("Intensity",          MakeSlider(w.VizIntensity, 50, 200, v => w.VizIntensity = v));
                AddProp("Background opacity", MakeSlider(w.BgOpacity, 0, 100, v => w.BgOpacity = v));
                AddProp("Corner radius",      MakeSlider(w.Radius, 0, 28, v => w.Radius = v));
                AddProp("Color tint",         MakeColorButton(w));
                break;

            case ObsWidgetType.SongWarsBracket:
                AddProp("Background opacity", MakeSlider(w.BgOpacity, 0, 100, v => w.BgOpacity = v));
                AddProp("Corner radius",      MakeSlider(w.Radius, 0, 28, v => w.Radius = v));
                AddProp("Max matches",        MakeSlider(w.MaxItems, 4, 32, v => w.MaxItems = v));
                break;
        }

        // Alignment section
        AddSectionLabel("ALIGN TO STREAM");
        content.Controls.Add(MakeAlignmentButtons());

        // Action buttons
        AddSectionLabel("ACTIONS");

        var actionRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Width = 224,
            Margin = new Padding(0, 0, 0, 4)
        };
        var dupBtn = new ModernButton { Width = 108, Height = 30, Text = "Duplicate", Margin = new Padding(0, 0, 4, 0) };
        ThemeControlStyler.ApplyGhostButtonTheme(dupBtn, palette, palette.AccentPrimaryColor);
        dupBtn.Click += (_, _) => canvas.DuplicateSelected();

        var deleteBtn = new ModernButton { Width = 108, Height = 30, Text = "Delete" };
        ThemeControlStyler.ApplyGhostButtonTheme(deleteBtn, palette, palette.DangerColor);
        deleteBtn.Click += (_, _) => canvas.DeleteSelected();

        actionRow.Controls.AddRange([dupBtn, deleteBtn]);
        content.Controls.Add(actionRow);

        var rowZ = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            Width = 224, Margin = new Padding(0, 2, 0, 0)
        };
        var bUp   = new ModernButton { Width = 108, Height = 28, Text = "Bring Forward", Margin = new Padding(0, 0, 4, 0) };
        var bDown = new ModernButton { Width = 108, Height = 28, Text = "Send Backward" };
        ThemeControlStyler.ApplyGhostButtonTheme(bUp,   palette, palette.AccentPrimaryColor);
        ThemeControlStyler.ApplyGhostButtonTheme(bDown, palette, palette.AccentPrimaryColor);
        bUp.Click   += (_, _) => canvas.BringSelectedForward();
        bDown.Click += (_, _) => canvas.SendSelectedBackward();
        rowZ.Controls.AddRange([bUp, bDown]);
        content.Controls.Add(rowZ);

        propsContent = content;
        propsPanel.Controls.Add(content);
        content.BringToFront();
    }

    private Control MakeAlignmentButtons()
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 3, RowCount = 2,
            AutoSize = true,
            Width = 224,
            Margin = new Padding(0, 0, 0, 4)
        };
        for (var i = 0; i < 3; i++)
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));

        var alignDefs = new (string Label, string Tip, StreamAlign Align)[]
        {
            ("⊢ Left",   "Align to left edge",       StreamAlign.Left),
            ("⊡ H-center","Center horizontally",     StreamAlign.CenterH),
            ("⊣ Right",  "Align to right edge",      StreamAlign.Right),
            ("⊤ Top",    "Align to top edge",        StreamAlign.Top),
            ("⊞ V-center","Center vertically",       StreamAlign.MiddleV),
            ("⊥ Bottom", "Align to bottom edge",     StreamAlign.Bottom),
        };

        var tip = new ToolTip();
        foreach (var (label, toolTip, align) in alignDefs)
        {
            var btn = new ModernButton { Text = label, Height = 26, Margin = new Padding(1, 1, 1, 1), Font = new Font("Segoe UI", 8F) };
            btn.Dock = DockStyle.Fill;
            ThemeControlStyler.ApplyGhostButtonTheme(btn, palette, palette.AccentPrimaryColor);
            tip.SetToolTip(btn, toolTip);
            var a = align;
            btn.Click += (_, _) => canvas.AlignToStream(a);
            grid.Controls.Add(btn);
        }

        return grid;
    }

    private Control MakeColorButton(ObsLayoutWidget w)
    {
        Color ToColor(string? hex)
        {
            if (!string.IsNullOrEmpty(hex))
                try { return ColorTranslator.FromHtml(hex); } catch { }
            return palette.AccentPrimaryColor;
        }

        var currentColor = ToColor(w.ColorHex);

        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Width = 224,
            WrapContents = false
        };

        var swatch = new Panel
        {
            Width = 30, Height = 30,
            BackColor = currentColor,
            Cursor = Cursors.Hand,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 6, 0)
        };

        var lblHex = new Label
        {
            Text = string.IsNullOrEmpty(w.ColorHex) ? "Theme accent" : w.ColorHex,
            AutoSize = true,
            ForeColor = palette.TextPrimaryColor,
            Font = new Font("Consolas", 8F),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 6, 8, 0)
        };

        var resetBtn = new ModernButton { Text = "↺", Width = 30, Height = 30, Margin = new Padding(0) };
        ThemeControlStyler.ApplyGhostButtonTheme(resetBtn, palette, palette.TextMutedColor);
        var tip = new ToolTip();
        tip.SetToolTip(resetBtn, "Reset to theme accent color");
        resetBtn.Click += (_, _) =>
        {
            w.ColorHex = null;
            swatch.BackColor = palette.AccentPrimaryColor;
            lblHex.Text = "Theme accent";
            canvas.NotifySelectedChanged();
        };

        void PickColor()
        {
            using var dlg = new ColorDialog
            {
                Color = swatch.BackColor,
                FullOpen = true,
                AnyColor = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            w.ColorHex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
            swatch.BackColor = dlg.Color;
            lblHex.Text = w.ColorHex;
            canvas.NotifySelectedChanged();
        }

        swatch.Click  += (_, _) => PickColor();
        lblHex.Click  += (_, _) => PickColor();

        row.Controls.Add(swatch);
        row.Controls.Add(lblHex);
        row.Controls.Add(resetBtn);
        return row;
    }

    private ModernComboBox MakeArtShapeCombo(ObsLayoutWidget w)
    {
        var cmb = new ModernComboBox { Height = 36 };
        ThemeControlStyler.ApplyComboBoxTheme(cmb, palette);
        cmb.Items.AddRange([
            new SelectionOption<string>("Rounded", "rounded"),
            new SelectionOption<string>("Square",  "square"),
            new SelectionOption<string>("Circle",  "circle")
        ]);
        for (var i = 0; i < cmb.Items.Count; i++)
            if (cmb.Items[i] is SelectionOption<string> opt && opt.Value == w.ArtShape)
            { cmb.SelectedIndex = i; break; }
        if (cmb.SelectedIndex < 0) cmb.SelectedIndex = 0;
        cmb.SelectedIndexChanged += (_, _) =>
        {
            if (cmb.SelectedItem is SelectionOption<string> opt) w.ArtShape = opt.Value;
            canvas.NotifySelectedChanged();
        };
        return cmb;
    }

    private ModernComboBox MakeVizKeyCombo(ObsLayoutWidget w)
    {
        var cmb = new ModernComboBox { Height = 36 };
        ThemeControlStyler.ApplyComboBoxTheme(cmb, palette);

        for (var i = 0; i < BuiltInVizNames.Length; i++)
        {
            var key = VisualizerChoice.BuiltIn(BuiltInVizModes[i]).Key;
            cmb.Items.Add(new SelectionOption<string>(BuiltInVizNames[i], key));
        }

        foreach (var viz in installedVisualizers)
        {
            var hasBanner = HasObsBanner(viz);
            if (hasBanner || settings.ObsOverlayAllowMissingCustomBanner)
            {
                var label = hasBanner ? viz.DisplayName : $"{viz.DisplayName} ⚠";
                cmb.Items.Add(new SelectionOption<string>(label, VisualizerChoice.Installed(viz.Id).Key));
            }
        }

        for (var i = 0; i < cmb.Items.Count; i++)
        {
            if (cmb.Items[i] is SelectionOption<string> opt &&
                string.Equals(opt.Value, w.VizKey, StringComparison.OrdinalIgnoreCase))
            { cmb.SelectedIndex = i; break; }
        }
        if (cmb.SelectedIndex < 0) cmb.SelectedIndex = 0;

        cmb.SelectedIndexChanged += (_, _) =>
        {
            if (cmb.SelectedItem is SelectionOption<string> opt) w.VizKey = opt.Value;
            canvas.NotifySelectedChanged();
        };
        return cmb;
    }

    // ── URL / save helpers ────────────────────────────────────────────────

    private void UpdateUrlDisplay()
    {
        var url = server?.BaseUrl ?? $"http://127.0.0.1:{settings.ObsOverlayPort}/obs/{settings.ObsOverlayToken}";
        txtUrl.Text = url;
        lblStatus.Text = GetStatusText();
    }

    private void SaveLayout()
    {
        var layout = new ObsLayout { Widgets = canvas.Widgets.ToList() };
        settings.ObsOverlayLayout = layout.ToJson();
    }

    private string GetStatusText() =>
        settings.EnableObsOverlay
            ? $"Server on port {settings.ObsOverlayPort} — add URL as OBS Browser Source"
            : "OBS overlay disabled. Toggle to enable.";

    private string BuildOverlayUrl(string overlayName)
    {
        var slug = ToOverlaySlug(overlayName);
        return server?.GetOverlayUrl(slug)
            ?? $"http://127.0.0.1:{settings.ObsOverlayPort}/obs/{settings.ObsOverlayToken}/o/{Uri.EscapeDataString(slug)}";
    }

    private static string ToOverlaySlug(string value)
    {
        var chars = (value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var slug = string.Join("-", new string(chars)
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(slug) ? "default" : slug;
    }

    // ── Preset bar ────────────────────────────────────────────────────────────

    private Panel BuildPresetBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            BackColor = Color.FromArgb(12, 12, 16),
            Padding = new Padding(8, 6, 8, 6)
        };
        var border = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = palette.BorderColor };

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.Transparent };

        _presetChipFlow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = 28,
            BackColor = Color.Transparent
        };

        scroll.Controls.Add(_presetChipFlow);
        bar.Controls.Add(scroll);
        bar.Controls.Add(border);

        RebuildPresetChips();
        return bar;
    }

    private void RebuildPresetChips()
    {
        if (_presetChipFlow is null) return;
        _presetChipFlow.SuspendLayout();
        _presetChipFlow.Controls.Clear();

        var lbl = new Label
        {
            Text = "PRESETS",
            AutoSize = true,
            Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
            ForeColor = palette.TextMutedColor,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 6, 10, 0)
        };
        _presetChipFlow.Controls.Add(lbl);

        foreach (var preset in BuiltInObsPresets.All)
        {
            var p = preset;
            _presetChipFlow.Controls.Add(
                MakePresetChip(p.Name, false, () => LoadPreset(p.Layout ?? ObsLayout.CreateDefault()), null));
        }

        var sep = new Panel { Width = 1, Height = 20, BackColor = palette.BorderColor, Margin = new Padding(6, 4, 6, 4) };
        _presetChipFlow.Controls.Add(sep);

        foreach (var preset in settings.ObsUserPresets)
        {
            var p = preset;
            _presetChipFlow.Controls.Add(
                MakePresetChip(p.Name, true,
                    () => LoadPreset(p.Layout ?? ObsLayout.CreateDefault()),
                    () => DeleteUserPreset(p.Name)));
        }

        var btnSave = new ModernButton { Text = "+ Save", Height = 26, Margin = new Padding(2, 0, 0, 0) };
        btnSave.Width = TextRenderer.MeasureText("+ Save", btnSave.Font).Width + 20;
        ThemeControlStyler.ApplyPrimaryButtonTheme(btnSave, palette, palette.AccentPrimaryColor);
        btnSave.Click += (_, _) => SaveCurrentAsPreset();
        _presetChipFlow.Controls.Add(btnSave);

        _presetChipFlow.ResumeLayout(true);
    }

    private Control MakePresetChip(string name, bool isUser, Action onClick, Action? onDelete)
    {
        var chipFont = new Font("Segoe UI", 8.5F);
        var chip = new ModernButton
        {
            Text = name,
            Height = 26,
            Width = Math.Max(70, TextRenderer.MeasureText(name, chipFont).Width + 22),
            Font = chipFont,
            Margin = new Padding(0, 0, 4, 0)
        };
        ThemeControlStyler.ApplyGhostButtonTheme(chip, palette,
            isUser ? palette.AccentSecondaryColor : palette.TextMutedColor);
        chip.Click += (_, _) => onClick();

        var ctx = new ContextMenuStrip();
        var copyItem = new ToolStripMenuItem("Copy OBS URL");
        copyItem.Click += (_, _) => Clipboard.SetText(BuildOverlayUrl(name));
        ctx.Items.Add(copyItem);

        if (onDelete is not null)
        {
            ctx.Items.Add(new ToolStripSeparator());
            var item = new ToolStripMenuItem("Delete");
            item.Click += (_, _) =>
            {
                if (MessageBox.Show(this, $"Delete preset '{name}'?", "Delete Preset",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    onDelete();
            };
            ctx.Items.Add(item);
        }

        chip.ContextMenuStrip = ctx;

        return chip;
    }

    private void LoadPreset(ObsLayout layout)
    {
        canvas.SetWidgets(layout.Widgets.Select(w => w.Clone()).ToList());
        RefreshPropertiesPanel();
        UpdateUrlDisplay();
    }

    private void SaveCurrentAsPreset()
    {
        var name = PromptPresetName();
        if (name is null) return;

        settings.ObsUserPresets.RemoveAll(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        settings.ObsUserPresets.Add(new ObsPreset
        {
            Name = name,
            LayoutJson = new ObsLayout { Widgets = canvas.Widgets.Select(w => w.Clone()).ToList() }.ToJson()
        });
        RebuildPresetChips();
    }

    private void DeleteUserPreset(string name)
    {
        settings.ObsUserPresets.RemoveAll(p => p.Name == name);
        RebuildPresetChips();
    }

    private string? PromptPresetName()
    {
        string? result = null;
        using var dlg = new Form
        {
            Text = "Save as Preset",
            Width = 340, Height = 144,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
            ShowIcon = false, ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = palette.WindowBackColor,
            ForeColor = palette.TextPrimaryColor
        };
        WindowChromeStyler.ApplyTheme(dlg, palette);

        var lbl   = new Label  { Text = "Preset name:", Left = 14, Top = 16, AutoSize = true, ForeColor = palette.TextPrimaryColor };
        var txt   = new TextBox { Left = 14, Top = 38, Width = 296, BackColor = palette.SurfaceAltBackColor, ForeColor = palette.TextPrimaryColor, BorderStyle = BorderStyle.FixedSingle };
        var btnOk = new ModernButton { Text = "Save",   Left = 154, Top = 78, Width = 76, Height = 30 };
        var btnNo = new ModernButton { Text = "Cancel", Left = 234, Top = 78, Width = 76, Height = 30 };
        ThemeControlStyler.ApplyPrimaryButtonTheme(btnOk, palette, palette.AccentPrimaryColor);
        ThemeControlStyler.ApplyGhostButtonTheme(btnNo, palette, palette.TextMutedColor);
        btnOk.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(txt.Text)) dlg.DialogResult = DialogResult.OK; };
        btnNo.Click += (_, _) => dlg.DialogResult = DialogResult.Cancel;
        txt.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Return && !string.IsNullOrWhiteSpace(txt.Text))
                dlg.DialogResult = DialogResult.OK;
            else if (e.KeyCode == Keys.Escape)
                dlg.DialogResult = DialogResult.Cancel;
        };
        dlg.Controls.AddRange([lbl, txt, btnOk, btnNo]);

        if (dlg.ShowDialog(this) == DialogResult.OK)
            result = txt.Text.Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
