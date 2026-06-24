using System.Drawing;
using System.IO;

namespace Spectralis;

internal sealed class VideoExportDialog : Form
{
    private readonly AudioTrackInfo _track;
    private readonly VisualizerMode _mode;
    private readonly VisualizerTheme _theme;
    private readonly bool _showPeaks;
    private readonly int _visualizerCycleSeconds;
    private readonly MidiPlaybackInstrument _midiInstrument;
    private readonly IReadOnlyList<VideoExportVisualizerOption> _visualizerOptions;

    private readonly ModernComboBox _cmbVisualizer;
    private readonly CheckBox _chkAutoCycle;
    private readonly ModernComboBox _cmbResolution;
    private readonly ModernComboBox _cmbFps;
    private readonly ModernSlider _sliderQuality;
    private readonly Label _lblQualityValue;
    private readonly CheckBox _chkTrackInfo;
    private readonly CheckBox _chkAlbumArt;
    private readonly CheckBox _chkPlaybackBar;
    private readonly TextBox _txtOutput;
    private readonly ModernButton _btnBrowse;
    private readonly ModernButton _btnExport;
    private readonly ModernButton _btnCancel;
    private readonly Label _lblStatus;
    private readonly System.Windows.Forms.ProgressBar _progressBar;

    private CancellationTokenSource? _cts;

    private static readonly (string Label, int W, int H)[] Resolutions =
    [
        ("1280 × 720 (720p HD)", 1280, 720),
        ("1920 × 1080 (1080p FHD)", 1920, 1080),
        ("2560 × 1440 (1440p QHD)", 2560, 1440),
        ("3840 × 2160 (4K UHD)", 3840, 2160),
    ];

    private static readonly (string Label, int Fps)[] FrameRates =
    [
        ("24 fps", 24),
        ("30 fps", 30),
        ("60 fps", 60),
    ];

    public VideoExportDialog(
        AudioTrackInfo track,
        VisualizerMode mode,
        VisualizerTheme theme,
        bool showPeaks,
        AppSettings settings,
        IReadOnlyList<VideoExportVisualizerOption> visualizerOptions,
        string? selectedVisualizerKey)
    {
        _track = track;
        _mode = mode;
        _theme = theme;
        _showPeaks = showPeaks;
        _visualizerCycleSeconds = Math.Clamp(settings.VisualizerCycleSeconds, 5, 60);
        _midiInstrument = settings.MidiInstrument;
        _visualizerOptions = visualizerOptions.Count > 0
            ? visualizerOptions
            : [VideoExportVisualizerOption.BuiltIn(mode)];

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(760, 620);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Export Visualizer Video";

        var palette = ThemePalette.Create(settings.ThemeMode, settings.ThemeAccent);
        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;
        WindowChromeStyler.ApplyTheme(this, palette);

        _cmbVisualizer = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Size = new Size(360, 36) };
        ThemeControlStyler.ApplyComboBoxTheme(_cmbVisualizer, palette);
        foreach (var option in _visualizerOptions)
            _cmbVisualizer.Items.Add(option);
        SelectVisualizer(selectedVisualizerKey);
        _cmbVisualizer.SelectedIndexChanged += (_, _) => UpdateAutoCycleState();

        _chkAutoCycle = Checkbox("Auto-cycle", palette, settings.EnableVisualizerAutoCycle);
        _chkAutoCycle.Size = new Size(120, 28);

        _cmbResolution = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Size = new Size(260, 36) };
        ThemeControlStyler.ApplyComboBoxTheme(_cmbResolution, palette);
        foreach (var (label, _, _) in Resolutions)
            _cmbResolution.Items.Add(label);
        _cmbResolution.SelectedIndex = 1; // 1080p default

        _cmbFps = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Size = new Size(130, 36) };
        ThemeControlStyler.ApplyComboBoxTheme(_cmbFps, palette);
        foreach (var (label, _) in FrameRates)
            _cmbFps.Items.Add(label);
        _cmbFps.SelectedIndex = 1; // 30 fps default

        _sliderQuality = new ModernSlider { Minimum = 50, Maximum = 100, Value = 85, Size = new Size(200, 28) };
        ThemeControlStyler.ApplySliderTheme(_sliderQuality, palette);
        _lblQualityValue = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9f),
            ForeColor = palette.TextSecondaryColor,
            Text = "85%",
            Margin = new Padding(8, 5, 0, 0)
        };
        _sliderQuality.Scroll += (_, _) => _lblQualityValue.Text = $"{_sliderQuality.Value}%";

        _chkTrackInfo = Checkbox("Track title && artist", palette, true);
        _chkAlbumArt = Checkbox("Album art thumbnail", palette, true);
        _chkPlaybackBar = Checkbox("Playback progress bar", palette, true);

        _txtOutput = new TextBox
        {
            Font = new Font("Segoe UI", 9f),
            Size = new Size(540, 30),
            Text = BuildDefaultOutputPath(track)
        };
        _txtOutput.BackColor = palette.SurfaceAltBackColor;
        _txtOutput.ForeColor = palette.TextPrimaryColor;
        _txtOutput.BorderStyle = BorderStyle.FixedSingle;

        _btnBrowse = new ModernButton { Size = new Size(90, 36), Text = "Browse..." };
        ThemeControlStyler.ApplyGhostButtonTheme(_btnBrowse, palette, palette.AccentPrimaryColor);
        _btnBrowse.Click += (_, _) => BrowseOutput();

        _btnExport = new ModernButton { Size = new Size(110, 40), Text = "Export" };
        ThemeControlStyler.ApplyPrimaryButtonTheme(_btnExport, palette, palette.AccentPrimaryColor);
        _btnExport.Click += async (_, _) => await StartExportAsync();

        _btnCancel = new ModernButton { Size = new Size(90, 40), Text = "Cancel" };
        ThemeControlStyler.ApplyGhostButtonTheme(_btnCancel, palette, palette.TextSoftColor);
        _btnCancel.Click += (_, _) => HandleCancel();

        _progressBar = new System.Windows.Forms.ProgressBar
        {
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 1000,
            Value = 0,
            Height = 10
        };

        _lblStatus = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = palette.TextMutedColor,
            Text = "Ready to export"
        };

        BuildLayout(palette);
        UpdateAutoCycleState();
    }

    private void BuildLayout(ThemePalette palette)
    {
        // Outer table: body (scrollable) + progress strip + footer
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // body
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));  // progress strip
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));  // footer
        root.BackColor = palette.WindowBackColor;

        // ── Body ──────────────────────────────────────────────────────────
        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(28, 22, 28, 0)
        };
        body.BackColor = palette.WindowBackColor;

        // Settings
        body.Controls.Add(SectionLabel("Settings", palette));

        var settingsGrid = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 10),
            Padding = Padding.Empty
        };
        settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));  // labels
        settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 370F)); // primary combo
        settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94F));  // secondary label
        settingsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F)); // secondary control
        settingsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        settingsGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        settingsGrid.BackColor = palette.WindowBackColor;
        settingsGrid.Controls.Add(FieldLabel("Visualizer", palette), 0, 0);
        settingsGrid.Controls.Add(_cmbVisualizer, 1, 0);
        settingsGrid.Controls.Add(_chkAutoCycle, 3, 0);
        settingsGrid.Controls.Add(FieldLabel("Resolution", palette), 0, 1);
        settingsGrid.Controls.Add(_cmbResolution, 1, 1);
        settingsGrid.Controls.Add(FieldLabel("Frame rate", palette), 2, 1);
        settingsGrid.Controls.Add(_cmbFps, 3, 1);
        body.Controls.Add(settingsGrid);

        var qualityRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16)
        };
        qualityRow.BackColor = palette.WindowBackColor;
        qualityRow.Controls.Add(FieldLabel("Quality", palette));
        _sliderQuality.Margin = new Padding(8, 0, 0, 0);
        qualityRow.Controls.Add(_sliderQuality);
        qualityRow.Controls.Add(_lblQualityValue);
        body.Controls.Add(qualityRow);

        // Overlays
        body.Controls.Add(SectionLabel("Overlays", palette));

        var overlayRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16)
        };
        overlayRow.BackColor = palette.WindowBackColor;
        overlayRow.Controls.Add(_chkTrackInfo);
        overlayRow.Controls.Add(_chkAlbumArt);
        overlayRow.Controls.Add(_chkPlaybackBar);
        body.Controls.Add(overlayRow);

        // Output
        body.Controls.Add(SectionLabel("Output file", palette));

        var outputRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        outputRow.BackColor = palette.WindowBackColor;
        _txtOutput.Margin = new Padding(0, 3, 8, 0);
        outputRow.Controls.Add(_txtOutput);
        outputRow.Controls.Add(_btnBrowse);
        body.Controls.Add(outputRow);

        body.Controls.Add(SubLabel("Exports as H.264 MP4 using FFmpeg (ffmpeg.exe must be in the app directory or system PATH). Audio is encoded with the video and matched to the rendered duration.", palette, 700));

        // ── Progress strip ────────────────────────────────────────────────
        var progressStrip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(28, 6, 28, 4),
            Margin = Padding.Empty,
            BackColor = palette.SurfaceBackColor
        };
        progressStrip.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        progressStrip.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _lblStatus.Dock = DockStyle.Left;
        progressStrip.Controls.Add(_lblStatus, 0, 0);
        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Margin = Padding.Empty;
        progressStrip.Controls.Add(_progressBar, 0, 1);

        // ── Footer ────────────────────────────────────────────────────────
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(20, 10, 20, 10),
            BackColor = palette.WindowBackColor
        };
        _btnExport.Margin = new Padding(0);
        _btnCancel.Margin = new Padding(0, 0, 10, 0);
        footer.Controls.Add(_btnExport);
        footer.Controls.Add(_btnCancel);

        root.Controls.Add(body, 0, 0);
        root.Controls.Add(progressStrip, 0, 1);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
    }

    private async Task StartExportAsync()
    {
        if (!TryPrepareOutputPath(out var outputPath))
            return;

        SetExporting(true);
        _cts = new CancellationTokenSource();

        var options = BuildOptions();
        options.OutputPath = outputPath;
        var progress = new Progress<float>(p =>
        {
            if (!IsHandleCreated) return;
            BeginInvoke(() =>
            {
                var pct = (int)(p * 100);
                _progressBar.Value = Math.Clamp((int)(p * 1000), 0, 1000);
                if (p < 1f)
                {
                    _lblStatus.Text = p >= 0.99f ? "Finalizing MP4..." : $"Rendering... {pct}%";
                    return;
                }
                _lblStatus.Text = "Saved.";
            });
        });

        try
        {
            await VideoExportEngine.ExportAsync(_track, _mode, _theme, _showPeaks, options, progress, _cts.Token);

            if (!_cts.IsCancellationRequested)
            {
                SetExporting(false);
                MessageBox.Show(this,
                    $"Video saved to:\n{options.OutputPath}",
                    "Export Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        catch (OperationCanceledException)
        {
            SetExporting(false);
            _progressBar.Value = 0;
            _lblStatus.Text = "Export cancelled.";
        }
        catch (Exception ex)
        {
            SetExporting(false);
            _progressBar.Value = 0;
            _lblStatus.Text = "Export failed.";
            MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    private void HandleCancel()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
        }
        else
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    private void SetExporting(bool exporting)
    {
        _cmbVisualizer.Enabled = !exporting;
        _chkAutoCycle.Enabled = !exporting && !GetSelectedVisualizer().UsesWebView && GetCycleVisualizerOptions().Count > 1;
        _cmbResolution.Enabled = !exporting;
        _cmbFps.Enabled = !exporting;
        _sliderQuality.Enabled = !exporting;
        _chkTrackInfo.Enabled = !exporting;
        _chkAlbumArt.Enabled = !exporting;
        _chkPlaybackBar.Enabled = !exporting;
        _txtOutput.Enabled = !exporting;
        _btnBrowse.Enabled = !exporting;
        _btnExport.Enabled = !exporting;
        _btnCancel.Text = exporting ? "Stop" : "Cancel";
        if (exporting)
        {
            _progressBar.Value = 0;
            _lblStatus.Text = "Preparing export...";
        }
    }

    private void BrowseOutput()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Export Video",
            Filter = "MP4 Video (*.mp4)|*.mp4",
            DefaultExt = "mp4",
            FileName = Path.GetFileName(_txtOutput.Text),
            InitialDirectory = Path.GetDirectoryName(_txtOutput.Text) is { Length: > 0 } d ? d
                                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        if (dlg.ShowDialog(this) == DialogResult.OK)
            _txtOutput.Text = dlg.FileName;
    }

    private VideoExportOptions BuildOptions()
    {
        var (_, w, h) = Resolutions[Math.Max(0, _cmbResolution.SelectedIndex)];
        var (_, fps) = FrameRates[Math.Max(0, _cmbFps.SelectedIndex)];
        var cycleOptions = GetCycleVisualizerOptions();
        return new VideoExportOptions
        {
            Width = w, Height = h, FrameRate = fps,
            Quality = _sliderQuality.Value,
            Visualizer = GetSelectedVisualizer(),
            AutoCycleVisualizers = _chkAutoCycle.Checked && cycleOptions.Count > 1,
            CycleVisualizers = cycleOptions,
            VisualizerCycleSeconds = _visualizerCycleSeconds,
            MidiInstrument = _midiInstrument,
            ShowTrackInfo = _chkTrackInfo.Checked,
            ShowAlbumArt = _chkAlbumArt.Checked,
            ShowPlaybackBar = _chkPlaybackBar.Checked,
            OutputPath = _txtOutput.Text.Trim()
        };
    }

    private void SelectVisualizer(string? selectedVisualizerKey)
    {
        if (_cmbVisualizer.Items.Count == 0)
            return;

        if (!string.IsNullOrWhiteSpace(selectedVisualizerKey))
        {
            for (var index = 0; index < _cmbVisualizer.Items.Count; index++)
            {
                if (_cmbVisualizer.Items[index] is VideoExportVisualizerOption option &&
                    string.Equals(option.Key, selectedVisualizerKey, StringComparison.OrdinalIgnoreCase))
                {
                    _cmbVisualizer.SelectedIndex = index;
                    return;
                }
            }
        }

        _cmbVisualizer.SelectedIndex = 0;
    }

    private VideoExportVisualizerOption GetSelectedVisualizer() =>
        _cmbVisualizer.SelectedItem as VideoExportVisualizerOption
        ?? _visualizerOptions.FirstOrDefault()
        ?? VideoExportVisualizerOption.BuiltIn(_mode);

    private IReadOnlyList<VideoExportVisualizerOption> GetCycleVisualizerOptions()
    {
        var selected = GetSelectedVisualizer();
        var renderable = _visualizerOptions
            .Where(static option => option.CanRenderInFrameExporter)
            .ToList();

        var selectedIndex = renderable.FindIndex(option =>
            string.Equals(option.Key, selected.Key, StringComparison.OrdinalIgnoreCase));
        if (selectedIndex > 0)
        {
            renderable = renderable
                .Skip(selectedIndex)
                .Concat(renderable.Take(selectedIndex))
                .ToList();
        }

        return renderable;
    }

    private void UpdateAutoCycleState()
    {
        var canCycle = !GetSelectedVisualizer().UsesWebView && GetCycleVisualizerOptions().Count > 1;
        _chkAutoCycle.Enabled = canCycle && _cts is null;
        if (!canCycle)
            _chkAutoCycle.Checked = false;
    }

    private bool TryPrepareOutputPath(out string outputPath)
    {
        outputPath = "";
        var rawPath = _txtOutput.Text.Trim();

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            MessageBox.Show(this, "Choose an output file path first.", "Export Video", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        try
        {
            if (!string.Equals(Path.GetExtension(rawPath), ".mp4", StringComparison.OrdinalIgnoreCase))
                rawPath = Path.ChangeExtension(rawPath, ".mp4");

            outputPath = Path.GetFullPath(rawPath);
            var sourcePath = Path.GetFullPath(_track.FilePath);
            if (string.Equals(outputPath, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Choose an output file that is different from the source audio file.", "Export Video", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                MessageBox.Show(this, "Choose a valid output folder.", "Export Video", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            Directory.CreateDirectory(outputDirectory);

            if (File.Exists(outputPath))
            {
                var result = MessageBox.Show(
                    this,
                    "Replace the existing MP4 at this location?",
                    "Export Video",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (result != DialogResult.Yes)
                    return false;
            }

            _txtOutput.Text = outputPath;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"That output path is not usable:\n{ex.Message}", "Export Video", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private static string BuildDefaultOutputPath(AudioTrackInfo track)
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        var name = string.IsNullOrWhiteSpace(track.DisplayName) ? "export" : SanitizeFileName(track.DisplayName);
        if (string.IsNullOrWhiteSpace(name))
            name = "export";

        return GetAvailableOutputPath(Path.Combine(dir, name + ".mp4"));
    }

    private static string SanitizeFileName(string name) =>
        new string(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();

    private static string GetAvailableOutputPath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{extension}");
    }

    private static CheckBox Checkbox(string text, ThemePalette palette, bool @checked) =>
        new()
        {
            AutoSize = false,
            Checked = @checked,
            Font = new Font("Segoe UI", 9.25f),
            Margin = new Padding(0, 0, 20, 8),
            Size = new Size(200, 28),
            Text = text,
            ForeColor = palette.TextPrimaryColor,
            BackColor = palette.WindowBackColor
        };

    private static Label FieldLabel(string text, ThemePalette? palette = null) =>
        new()
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.25f),
            ForeColor = palette?.TextPrimaryColor ?? Color.White,
            BackColor = palette?.WindowBackColor ?? Color.Transparent,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 8, 0)
        };

    private static Label SectionLabel(string text, ThemePalette palette) =>
        new()
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = palette.TextPrimaryColor,
            Margin = new Padding(0, 0, 0, 10),
            Text = text
        };

    private static Label SubLabel(string text, ThemePalette palette, int maxWidth = 560) =>
        new()
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = palette.TextMutedColor,
            Margin = new Padding(0, 0, 0, 0),
            MaximumSize = new Size(maxWidth, 0),
            Text = text
        };

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_cts is not null && !_cts.IsCancellationRequested)
        {
            e.Cancel = true;
            _cts.Cancel();
            return;
        }
        base.OnFormClosing(e);
    }
}
