using System.Globalization;
using System.IO;
using System.Text;

namespace Spectralis;

internal sealed class LyricsTimingStudioDialog : Form
{
    private enum TimingMode { Line, Word, Syllable }

    private readonly AudioTrackInfo? track;
    private readonly Func<double> getPositionSeconds;
    private readonly Func<bool> isPlaybackRunning;
    private readonly Action togglePlayback;
    private readonly Action<double>? seek;
    private readonly ThemePalette palette;
    private readonly List<TimedUnit> units = [];
    private readonly System.Windows.Forms.Timer uiTimer;

    private string[] rawLines = [];
    private TimingMode currentMode = TimingMode.Line;

    private readonly TextBox txtLyrics;
    private readonly LyricsTimingCanvas canvas;
    private readonly Label lblPosition;
    private readonly Label lblStatus;
    private readonly ComboBox cboMode;
    private readonly ModernButton btnLoadLines;
    private readonly ModernButton btnPlayPause;
    private readonly ModernButton btnTapLine;
    private readonly ModernButton btnSeekLine;
    private readonly ModernButton btnClearLine;
    private readonly ModernButton btnNudgeBackSmall;
    private readonly ModernButton btnNudgeForwardSmall;
    private readonly ModernButton btnNudgeBackLarge;
    private readonly ModernButton btnNudgeForwardLarge;
    private readonly ModernButton btnCopyLrc;
    private readonly ModernButton btnExport;
    private readonly ModernButton btnClose;

    public LyricsTimingStudioDialog(
        AudioTrackInfo? track,
        Func<double> getPositionSeconds,
        Func<bool> isPlaybackRunning,
        Action togglePlayback,
        Action<double>? seek,
        ThemePalette palette)
    {
        this.track = track;
        this.getPositionSeconds = getPositionSeconds;
        this.isPlaybackRunning = isPlaybackRunning;
        this.togglePlayback = togglePlayback;
        this.seek = seek;
        this.palette = palette;

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1020, 660);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        MinimumSize = new Size(860, 560);
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Lyrics Timing Studio";

        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;
        WindowChromeStyler.ApplyTheme(this, palette);

        txtLyrics = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.25F, FontStyle.Regular, GraphicsUnit.Point),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = BuildInitialLyricsText(track),
            WordWrap = true
        };
        txtLyrics.BackColor = palette.SurfaceAltBackColor;
        txtLyrics.ForeColor = palette.TextPrimaryColor;

        canvas = new LyricsTimingCanvas
        {
            BackColor = palette.SurfaceAltBackColor,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill
        };
        canvas.UnitSelected += (_, index) =>
        {
            // Clicking a chip selects it so the next tap targets it.
        };

        lblPosition = new Label
        {
            AutoSize = true,
            Font = new Font("Consolas", 12F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = palette.AccentPrimaryColor,
            Margin = new Padding(0),
            Text = "00:00.00"
        };

        lblStatus = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.25F),
            ForeColor = palette.TextMutedColor,
            Margin = new Padding(0),
            Text = "Load lines, then press Tab to tap timing as the track plays."
        };

        cboMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 8.75F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0),
            Width = 96
        };
        cboMode.Items.AddRange(["Line", "Word", "Syllable"]);
        cboMode.SelectedIndex = 0;
        cboMode.BackColor = palette.SurfaceAltBackColor;
        cboMode.ForeColor = palette.TextPrimaryColor;
        cboMode.SelectedIndexChanged += (_, _) => OnModeChanged();

        btnLoadLines = Button("Load Lines", primary: false);
        btnPlayPause = Button("Play", primary: false);
        btnTapLine = Button("Tap Current Line", primary: true);
        btnSeekLine = Button("Seek To Line", primary: false);
        btnClearLine = Button("Clear Time", primary: false);
        btnNudgeBackSmall = Button("-0.10s", primary: false);
        btnNudgeForwardSmall = Button("+0.10s", primary: false);
        btnNudgeBackLarge = Button("-0.50s", primary: false);
        btnNudgeForwardLarge = Button("+0.50s", primary: false);
        btnCopyLrc = Button("Copy LRC", primary: false);
        btnExport = Button("Export LRC", primary: true);
        btnClose = Button("Close", primary: false);

        btnLoadLines.Click += (_, _) => LoadUnitsFromText(replaceExistingTiming: false);
        btnPlayPause.Click += (_, _) => togglePlayback();
        btnTapLine.Click += (_, _) => MarkCurrentUnit();
        btnSeekLine.Click += (_, _) => SeekToSelectedUnit();
        btnClearLine.Click += (_, _) => ClearSelectedUnitTime();
        btnNudgeBackSmall.Click += (_, _) => ShiftTimedUnits(-0.10);
        btnNudgeForwardSmall.Click += (_, _) => ShiftTimedUnits(0.10);
        btnNudgeBackLarge.Click += (_, _) => ShiftTimedUnits(-0.50);
        btnNudgeForwardLarge.Click += (_, _) => ShiftTimedUnits(0.50);
        btnCopyLrc.Click += (_, _) => CopyLrc();
        btnExport.Click += (_, _) => ExportLrc();
        btnClose.Click += (_, _) => Close();

        BuildLayout();
        LoadInitialUnits();
        UpdateButtonState();

        uiTimer = new System.Windows.Forms.Timer { Interval = 80 };
        uiTimer.Tick += (_, _) => UpdatePositionLabel();
        uiTimer.Start();
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Tab taps the current unit from anywhere in the dialog.
    /// Tab is always intercepted here so it never shifts focus between controls.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Tab)
        {
            MarkCurrentUnit();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            uiTimer.Dispose();

        base.Dispose(disposing);
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    private ModernButton Button(string text, bool primary)
    {
        var button = new ModernButton
        {
            Margin = new Padding(0, 0, 8, 8),
            Size = new Size(text.Length > 11 ? 142 : 104, 36),
            Text = text
        };

        if (primary)
            ThemeControlStyler.ApplyPrimaryButtonTheme(button, palette, palette.AccentPrimaryColor);
        else
            ThemeControlStyler.ApplyGhostButtonTheme(button, palette, palette.AccentSoftColor);

        return button;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 20),
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle());
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle());
        root.RowStyles.Add(new RowStyle());

        // ── Header ──────────────────────────────────────────────────────────
        var header = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 14),
            RowCount = 1
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titleBlock = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            Margin = Padding.Empty,
            WrapContents = false
        };
        titleBlock.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = palette.TextPrimaryColor,
            Margin = Padding.Empty,
            Text = "Lyrics Timing Studio"
        });
        titleBlock.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.75F),
            ForeColor = palette.TextMutedColor,
            Margin = new Padding(0, 4, 0, 0),
            Text = BuildTrackCaption()
        });

        header.Controls.Add(titleBlock, 0, 0);
        header.Controls.Add(lblPosition, 1, 0);

        // ── Editor split ─────────────────────────────────────────────────────
        var editorSplit = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            RowCount = 1
        };
        editorSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        editorSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));

        var lyricsPanel = PanelWithLabel("Plain Lyrics  (use | to split syllables)", txtLyrics);
        var timingPanel = BuildTimingPanel();
        lyricsPanel.Margin = new Padding(0, 0, 12, 0);
        timingPanel.Margin = Padding.Empty;
        editorSplit.Controls.Add(lyricsPanel, 0, 0);
        editorSplit.Controls.Add(timingPanel, 1, 0);

        // ── Controls ─────────────────────────────────────────────────────────
        var controls = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 14, 0, 4),
            WrapContents = true
        };
        controls.Controls.AddRange([
            btnLoadLines,
            btnPlayPause,
            btnTapLine,
            btnSeekLine,
            btnClearLine,
            btnNudgeBackLarge,
            btnNudgeBackSmall,
            btnNudgeForwardSmall,
            btnNudgeForwardLarge,
            btnCopyLrc,
            btnExport
        ]);

        // ── Footer ────────────────────────────────────────────────────────────
        var footer = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0),
            RowCount = 1
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(lblStatus, 0, 0);
        footer.Controls.Add(btnClose, 1, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(editorSplit, 0, 1);
        root.Controls.Add(controls, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);
    }

    private TableLayoutPanel BuildTimingPanel()
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle());
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // Header: label + mode selector
        var headerRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 8),
            WrapContents = false
        };
        headerRow.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.25F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = palette.TextSecondaryColor,
            Margin = new Padding(0, 4, 10, 0),
            Text = "Timed Lines"
        });
        headerRow.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.75F),
            ForeColor = palette.TextMutedColor,
            Margin = new Padding(0, 5, 4, 0),
            Text = "Mode:"
        });
        headerRow.Controls.Add(cboMode);

        panel.Controls.Add(headerRow, 0, 0);
        panel.Controls.Add(canvas, 0, 1);
        return panel;
    }

    private TableLayoutPanel PanelWithLabel(string label, Control content)
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        panel.RowStyles.Add(new RowStyle());
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.25F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = palette.TextSecondaryColor,
            Margin = new Padding(0, 0, 0, 8),
            Text = label
        }, 0, 0);
        panel.Controls.Add(content, 0, 1);
        return panel;
    }

    private string BuildTrackCaption()
    {
        if (track is null)
            return "No track loaded. You can still prepare plain lyrics and export later.";

        return string.IsNullOrWhiteSpace(track.Artist)
            ? track.DisplayName
            : $"{track.Artist} - {track.DisplayName}";
    }

    // ── Mode ─────────────────────────────────────────────────────────────────

    private void OnModeChanged()
    {
        var newMode = cboMode.SelectedIndex switch
        {
            1 => TimingMode.Word,
            2 => TimingMode.Syllable,
            _ => TimingMode.Line
        };

        if (newMode == currentMode) return;

        if (units.Any(u => u.TimeSeconds.HasValue))
        {
            var result = MessageBox.Show(
                this,
                "Changing timing mode will reload the unit list and clear existing timestamps. Continue?",
                "Change Timing Mode",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                cboMode.SelectedIndex = currentMode switch
                {
                    TimingMode.Word => 1,
                    TimingMode.Syllable => 2,
                    _ => 0
                };
                return;
            }
        }

        currentMode = newMode;
        UpdateTapButtonText();
        LoadUnitsFromText(replaceExistingTiming: true);

        lblStatus.Text = currentMode == TimingMode.Syllable
            ? "Syllable mode: use | to manually split (e.g. hel|lo). Known words auto-split. Press Tab to tap."
            : currentMode == TimingMode.Word
                ? "Word mode: click a word to target it, then press Tab to tap. Auto-advances after each tap."
                : "Line mode: press Tab or click Tap to mark each line. Auto-advances after each tap.";
    }

    private void UpdateTapButtonText()
    {
        btnTapLine.Text = currentMode switch
        {
            TimingMode.Word => "Tap Word",
            TimingMode.Syllable => "Tap Syllable",
            _ => "Tap Current Line"
        };
        // Adjust button width for shorter text
        btnTapLine.Width = btnTapLine.Text.Length > 11 ? 142 : 104;
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    private void LoadInitialUnits()
    {
        var lyrics = track?.Lyrics;
        if (lyrics is { HasLines: true, IsDescription: false })
        {
            rawLines = lyrics.Lines.Select(static l => l.Text).ToArray();

            if (currentMode == TimingMode.Line)
            {
                units.Clear();
                for (var i = 0; i < lyrics.Lines.Count; i++)
                {
                    var line = lyrics.Lines[i];
                    units.Add(new TimedUnit(i, line.Text, line.Text, isLineStart: true, hasLeadingSpace: false)
                    { TimeSeconds = line.StartTime });
                }
                RefreshCanvas(selectIndex: 0);
                return;
            }

            if (currentMode == TimingMode.Word && lyrics.HasWordTimings)
            {
                units.Clear();
                for (var i = 0; i < lyrics.Lines.Count; i++)
                {
                    var line = lyrics.Lines[i];
                    var first = true;
                    foreach (var seg in line.Segments)
                    {
                        var trimmed = seg.Text.Trim();
                        if (trimmed.Length == 0) continue;
                        units.Add(new TimedUnit(i, trimmed, trimmed, isLineStart: first, hasLeadingSpace: !first)
                        { TimeSeconds = seg.StartTime });
                        first = false;
                    }
                }
                RefreshCanvas(selectIndex: 0);
                return;
            }
        }

        LoadUnitsFromText(replaceExistingTiming: true);
    }

    private void LoadUnitsFromText(bool replaceExistingTiming)
    {
        var textLines = ParsePlainLyrics(txtLyrics.Text);
        if (textLines.Length == 0)
        {
            lblStatus.Text = "Paste lyrics first, then load.";
            return;
        }

        var previousTimes = replaceExistingTiming
            ? Array.Empty<double?>()
            : units.Select(static u => u.TimeSeconds).ToArray();

        rawLines = textLines;
        var newUnits = BuildUnitsFromText(textLines);

        units.Clear();
        for (var i = 0; i < newUnits.Count; i++)
        {
            var u = newUnits[i];
            if (i < previousTimes.Length && previousTimes[i].HasValue)
                u.TimeSeconds = previousTimes[i];
            units.Add(u);
        }

        RefreshCanvas(selectIndex: 0);
        lblStatus.Text = currentMode == TimingMode.Line
            ? $"Loaded {units.Count} lines.  Press Tab to tap."
            : $"Loaded {units.Count} {(currentMode == TimingMode.Word ? "words" : "syllables")} across {rawLines.Length} lines.  Press Tab to tap.";
    }

    private List<TimedUnit> BuildUnitsFromText(string[] textLines)
    {
        var result = new List<TimedUnit>();

        for (var li = 0; li < textLines.Length; li++)
        {
            var lineText = textLines[li];

            if (currentMode == TimingMode.Line)
            {
                result.Add(new TimedUnit(li, lineText, lineText, isLineStart: true, hasLeadingSpace: false));
                continue;
            }

            var wordTokens = lineText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var isFirstWordInLine = true;

            foreach (var word in wordTokens)
            {
                if (currentMode == TimingMode.Word)
                {
                    var clean = word.Replace("|", "", StringComparison.Ordinal);
                    result.Add(new TimedUnit(li, clean, clean,
                        isLineStart: isFirstWordInLine,
                        hasLeadingSpace: !isFirstWordInLine));
                    isFirstWordInLine = false;
                }
                else // Syllable
                {
                    var syllables = SyllableBank.Split(word);
                    for (var si = 0; si < syllables.Length; si++)
                    {
                        var syl = syllables[si];
                        var isFirst = isFirstWordInLine && si == 0;
                        // hasLeadingSpace = true for the first syllable of non-first words
                        var hasSpace = !isFirst && si == 0;
                        result.Add(new TimedUnit(li, syl, syl, isLineStart: isFirst, hasLeadingSpace: hasSpace));
                    }
                    isFirstWordInLine = false;
                }
            }
        }

        return result;
    }

    // ── Canvas refresh ────────────────────────────────────────────────────────

    private void RefreshCanvas(int selectIndex)
    {
        var canvasUnits = units.Select(static u => new LyricsTimingCanvas.Unit
        {
            Text = u.OutputText,
            IsTimed = u.TimeSeconds.HasValue,
            IsLineStart = u.IsLineStart,
            // IsWordStart: true for every unit except non-first syllables of the same word
            IsWordStart = u.IsLineStart || u.HasLeadingSpace
        }).ToList();

        canvas.SetUnits(canvasUnits, palette);

        if (units.Count > 0 && selectIndex >= 0)
            canvas.SelectAndScroll(Math.Clamp(selectIndex, 0, units.Count - 1));

        UpdateButtonState();
    }

    // ── Timing actions ────────────────────────────────────────────────────────

    private void MarkCurrentUnit()
    {
        if (units.Count == 0)
        {
            LoadUnitsFromText(replaceExistingTiming: true);
            if (units.Count == 0) return;
        }

        var index = canvas.SelectedIndex;
        if (index < 0)
            index = units.FindIndex(static u => u.TimeSeconds is null);
        if (index < 0)
            index = Math.Max(0, units.Count - 1);

        units[index].TimeSeconds = Math.Max(0, getPositionSeconds());
        canvas.UpdateTimed(index, isTimed: true);

        var next = Math.Min(index + 1, units.Count - 1);
        canvas.SelectAndScroll(next);
        UpdateButtonState();

        var label = currentMode == TimingMode.Line ? "line" : currentMode == TimingMode.Word ? "word" : "syllable";
        lblStatus.Text = $"Marked {label} {index + 1} at {FormatTimestamp(units[index].TimeSeconds!.Value)}.";
    }

    private void SeekToSelectedUnit()
    {
        var index = canvas.SelectedIndex;
        if (index < 0 || units[index].TimeSeconds is not { } seconds)
        {
            lblStatus.Text = "Select a timed entry first.";
            return;
        }

        seek?.Invoke(seconds);
        lblStatus.Text = $"Seeked to entry {index + 1}.";
    }

    private void ClearSelectedUnitTime()
    {
        var index = canvas.SelectedIndex;
        if (index < 0) return;

        units[index].TimeSeconds = null;
        canvas.UpdateTimed(index, isTimed: false);
        UpdateButtonState();
        lblStatus.Text = $"Cleared timing for entry {index + 1}.";
    }

    private void ShiftTimedUnits(double deltaSeconds)
    {
        var changed = 0;
        foreach (var unit in units)
        {
            if (unit.TimeSeconds is not { } s) continue;
            unit.TimeSeconds = Math.Max(0, s + deltaSeconds);
            changed++;
        }

        UpdateButtonState();
        lblStatus.Text = changed == 0
            ? "No timed entries to nudge."
            : $"Nudged {changed} entries by {deltaSeconds:+0.00;-0.00}s.";
    }

    // ── Export ────────────────────────────────────────────────────────────────

    private void CopyLrc()
    {
        var lrc = BuildLrcText(confirmOmittedLines: true);
        if (lrc is null) return;
        Clipboard.SetText(lrc);
        lblStatus.Text = "LRC copied to clipboard.";
    }

    private void ExportLrc()
    {
        var lrc = BuildLrcText(confirmOmittedLines: true);
        if (lrc is null) return;

        var defaultPath = GetDefaultLrcPath();
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "lrc",
            FileName = Path.GetFileName(defaultPath),
            Filter = "LRC lyrics|*.lrc|Text files|*.txt|All files|*.*",
            InitialDirectory = Path.GetDirectoryName(defaultPath),
            OverwritePrompt = true,
            Title = "Export LRC"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        File.WriteAllText(dialog.FileName, lrc, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        lblStatus.Text = $"Exported {Path.GetFileName(dialog.FileName)}.";
    }

    private string? BuildLrcText(bool confirmOmittedLines) =>
        currentMode == TimingMode.Line
            ? BuildLineModeText(confirmOmittedLines)
            : BuildInlineModeText(confirmOmittedLines);

    private string? BuildLineModeText(bool confirmOmittedLines)
    {
        var timed = units
            .Where(static u => u.TimeSeconds is not null && !string.IsNullOrWhiteSpace(u.OutputText))
            .OrderBy(static u => u.TimeSeconds)
            .ToArray();

        var omitted = units.Count(static u => u.TimeSeconds is null && !string.IsNullOrWhiteSpace(u.OutputText));

        if (timed.Length == 0)
        {
            lblStatus.Text = "Tap at least one line before exporting.";
            return null;
        }

        if (omitted > 0 && confirmOmittedLines)
        {
            var r = MessageBox.Show(this,
                $"{omitted} lyric line(s) have no timestamps and will be omitted. Continue?",
                "Export LRC", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return null;
        }

        var sb = new StringBuilder();
        AppendTrackMetadata(sb);
        foreach (var u in timed)
            sb.Append('[').Append(FormatTimestamp(u.TimeSeconds!.Value)).Append(']').AppendLine(u.OutputText.Trim());

        return sb.ToString();
    }

    private string? BuildInlineModeText(bool confirmOmittedLines)
    {
        var lineGroups = units
            .GroupBy(static u => u.LineIndex)
            .Select(static g => g.ToList())
            .ToList();

        var timedGroups = lineGroups
            .Where(static g => g.First().TimeSeconds.HasValue)
            .OrderBy(static g => g.First().TimeSeconds!.Value)
            .ToList();

        var omittedLines = lineGroups.Count(static g =>
            !g.Any(static u => u.TimeSeconds.HasValue) &&
            g.Any(static u => !string.IsNullOrWhiteSpace(u.OutputText)));

        if (timedGroups.Count == 0)
        {
            lblStatus.Text = currentMode == TimingMode.Word
                ? "Tap at least one word before exporting."
                : "Tap at least one syllable before exporting.";
            return null;
        }

        if (omittedLines > 0 && confirmOmittedLines)
        {
            var r = MessageBox.Show(this,
                $"{omittedLines} line(s) have no timing and will be omitted. Continue?",
                "Export LRC", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return null;
        }

        var sb = new StringBuilder();
        AppendTrackMetadata(sb);

        foreach (var lineUnits in timedGroups)
        {
            var lineTime = lineUnits.First().TimeSeconds!.Value;
            sb.Append('[').Append(FormatTimestamp(lineTime)).Append(']');

            foreach (var unit in lineUnits)
            {
                if (unit.HasLeadingSpace) sb.Append(' ');
                if (unit.TimeSeconds.HasValue)
                    sb.Append('<').Append(FormatTimestamp(unit.TimeSeconds.Value)).Append('>');
                sb.Append(unit.OutputText);
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private void AppendTrackMetadata(StringBuilder sb)
    {
        if (track is null) return;
        AppendMetadata(sb, "ti", track.DisplayName);
        AppendMetadata(sb, "ar", track.Artist);
        AppendMetadata(sb, "al", track.Album);
        if (sb.Length > 0) sb.AppendLine();
    }

    private static void AppendMetadata(StringBuilder sb, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append('[').Append(key).Append(':')
          .Append(value.Trim().Replace("]", ")", StringComparison.Ordinal))
          .AppendLine("]");
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private void UpdateButtonState()
    {
        var hasUnits = units.Count > 0;
        btnTapLine.Enabled = hasUnits || !string.IsNullOrWhiteSpace(txtLyrics.Text);
        btnPlayPause.Enabled = track is not null;
        btnSeekLine.Enabled = hasUnits && seek is not null;
        btnClearLine.Enabled = hasUnits;
        btnCopyLrc.Enabled = hasUnits;
        btnExport.Enabled = hasUnits;
    }

    private void UpdatePositionLabel()
    {
        lblPosition.Text = FormatTimestamp(getPositionSeconds());
        btnPlayPause.Text = isPlaybackRunning() ? "Pause" : "Play";
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private string GetDefaultLrcPath()
    {
        if (track is not null &&
            File.Exists(track.FilePath) &&
            !track.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return Path.ChangeExtension(track.FilePath, ".lrc");
        }

        var title = string.IsNullOrWhiteSpace(track?.DisplayName) ? "lyrics" : SanitizeFileName(track.DisplayName);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"{title}.lrc");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "lyrics" : cleaned;
    }

    private static string[] ParsePlainLyrics(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
             .Replace('\r', '\n')
             .Split('\n')
             .Select(static line => StripLrcPrefix(line).Trim())
             .Where(static line => !string.IsNullOrWhiteSpace(line))
             .ToArray();

    private static string StripLrcPrefix(string line)
    {
        var cursor = 0;
        while (cursor < line.Length && line[cursor] == '[')
        {
            var close = line.IndexOf(']', cursor + 1);
            if (close < 0) break;
            var tag = line[(cursor + 1)..close];
            if (!LooksLikeTimestamp(tag) && tag.Contains(':', StringComparison.Ordinal))
                return "";
            cursor = close + 1;
        }
        return line[cursor..];
    }

    private static bool LooksLikeTimestamp(string value)
    {
        var parts = value.Split(':');
        return parts.Length is 2 or 3 &&
               int.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out _) &&
               double.TryParse(parts[^1].Replace(',', '.'),
                   NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);
    }

    private static string BuildInitialLyricsText(AudioTrackInfo? track)
    {
        var lyrics = track?.Lyrics;
        if (lyrics is { HasLines: true })
            return string.Join(Environment.NewLine, lyrics.Lines.Select(static line => line.Text));
        return "";
    }

    private static string FormatTimestamp(double seconds)
    {
        var clamped = Math.Max(0, seconds);
        var minutes = (int)(clamped / 60d);
        var remainder = clamped - (minutes * 60d);
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00.00}", minutes, remainder);
    }

    // ── TimedUnit ─────────────────────────────────────────────────────────────

    private sealed class TimedUnit(
        int lineIndex,
        string displayText,
        string outputText,
        bool isLineStart,
        bool hasLeadingSpace)
    {
        public int LineIndex { get; } = lineIndex;
        public string DisplayText { get; } = displayText;
        public string OutputText { get; } = outputText;
        public bool IsLineStart { get; } = isLineStart;
        /// <summary>True for the first syllable of non-first words (used for spacing in LRC output).</summary>
        public bool HasLeadingSpace { get; } = hasLeadingSpace;
        public double? TimeSeconds { get; set; }
    }
}
