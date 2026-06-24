using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

internal sealed class LyricsInspectorDialog : Form
{
    private readonly ThemePalette palette;
    private readonly AudioTrackInfo track;
    private readonly LyricsDocument lyrics;
    private readonly double positionSeconds;
    private readonly FlowLayoutPanel lyricsPanel;
    private readonly Label annotationTitle;
    private readonly Label annotationTime;
    private readonly Label annotationBody;
    private readonly List<LyricsInspectorRow> rows = [];
    private LyricsInspectorRow? selectedRow;

    public LyricsInspectorDialog(AudioTrackInfo track, ThemePalette palette, double positionSeconds)
    {
        this.track = track;
        this.palette = palette;
        this.positionSeconds = Math.Max(0, positionSeconds);
        lyrics = track.Lyrics ?? new LyricsDocument([]);

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = true;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        WindowState = FormWindowState.Normal;

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(28, 22, 28, 24),
            RowCount = 2
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var header = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 18),
            RowCount = 2
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle());
        header.RowStyles.Add(new RowStyle());

        var title = new Label
        {
            AutoEllipsis = true,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 22F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = palette.TextPrimaryColor,
            Margin = Padding.Empty,
            Text = track.DisplayName,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        };

        var subtitle = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = palette.TextSecondaryColor,
            Margin = new Padding(0, 4, 0, 0),
            Text = BuildSubtitle(),
            UseMnemonic = false
        };

        var closeButton = new ModernButton
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
            IsGhost = true,
            Margin = new Padding(18, 0, 0, 0),
            Size = new Size(82, 34),
            Text = "Close"
        };
        ThemeControlStyler.ApplyGhostButtonTheme(closeButton, palette, palette.AccentSoftColor);
        closeButton.Click += (_, _) => Close();

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(closeButton, 1, 0);
        header.Controls.Add(subtitle, 0, 1);
        header.SetColumnSpan(subtitle, 2);

        var content = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 1
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var lyricsScroll = new Panel
        {
            AutoScroll = true,
            BackColor = palette.SurfaceBackColor,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 18, 0),
            Padding = new Padding(14)
        };

        lyricsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = palette.SurfaceBackColor,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            WrapContents = false
        };
        lyricsPanel.Resize += (_, _) => ResizeRows();
        lyricsScroll.SizeChanged += (_, _) => ResizeRows();
        lyricsScroll.Controls.Add(lyricsPanel);

        var annotationPanel = new TableLayoutPanel
        {
            BackColor = palette.SurfaceRaisedColor,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(22, 20, 22, 22),
            RowCount = 4
        };
        annotationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        annotationPanel.RowStyles.Add(new RowStyle());
        annotationPanel.RowStyles.Add(new RowStyle());
        annotationPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        annotationPanel.RowStyles.Add(new RowStyle());

        var annotationCaption = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = palette.AccentPrimaryColor,
            Margin = Padding.Empty,
            Text = "ANNOTATION",
            UseMnemonic = false
        };

        annotationTitle = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = palette.TextPrimaryColor,
            Margin = new Padding(0, 14, 0, 0),
            MaximumSize = new Size(340, 0),
            UseMnemonic = false
        };

        annotationTime = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = palette.TextMutedColor,
            Margin = new Padding(0, 8, 0, 0),
            UseMnemonic = false
        };

        annotationBody = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 12F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = palette.TextSecondaryColor,
            Margin = new Padding(0, 22, 0, 0),
            MaximumSize = new Size(340, 0),
            UseMnemonic = false
        };

        annotationPanel.Controls.Add(annotationCaption, 0, 0);
        annotationPanel.Controls.Add(annotationTitle, 0, 1);
        annotationPanel.Controls.Add(annotationBody, 0, 2);
        annotationPanel.Controls.Add(annotationTime, 0, 3);

        content.Controls.Add(lyricsScroll, 0, 0);
        content.Controls.Add(annotationPanel, 1, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(content, 0, 1);
        Controls.Add(root);

        BuildRows();
        SelectInitialAnnotation();
        Shown += (_, _) => ScrollToCurrentLine();
    }

    public void FitToOwnerClient(Form owner)
    {
        var ownerClientTopLeft = owner.PointToScreen(Point.Empty);
        Bounds = new Rectangle(ownerClientTopLeft, owner.ClientSize);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void BuildRows()
    {
        lyricsPanel.SuspendLayout();
        lyricsPanel.Controls.Clear();
        rows.Clear();

        var currentIndex = lyrics.FindLineIndex(positionSeconds);
        for (var i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            var row = new LyricsInspectorRow(
                FormatTimestamp(line.StartTime),
                line.Text,
                !string.IsNullOrWhiteSpace(line.Explanation),
                i == currentIndex,
                palette)
            {
                Margin = new Padding(0, 0, 0, 8),
                Tag = line
            };

            if (!string.IsNullOrWhiteSpace(line.Explanation))
            {
                row.Click += (_, _) => SelectRow(row);
            }

            lyricsPanel.Controls.Add(row);
            rows.Add(row);
        }

        lyricsPanel.ResumeLayout();
        ResizeRows();
    }

    private void ResizeRows()
    {
        var parent = lyricsPanel.Parent;
        if (parent is null || rows.Count == 0)
        {
            return;
        }

        var width = Math.Max(280, parent.ClientSize.Width - parent.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4);
        foreach (var row in rows)
        {
            row.Width = width;
            row.UpdatePreferredHeight(width);
        }
    }

    private void SelectInitialAnnotation()
    {
        var currentIndex = lyrics.FindLineIndex(positionSeconds);
        var annotatedCurrent = currentIndex >= 0 && currentIndex < rows.Count && rows[currentIndex].HasAnnotation
            ? rows[currentIndex]
            : null;

        SelectRow(annotatedCurrent ?? rows.FirstOrDefault(static row => row.HasAnnotation));
    }

    private void SelectRow(LyricsInspectorRow? row)
    {
        if (selectedRow is not null)
        {
            selectedRow.Selected = false;
        }

        selectedRow = row;
        if (selectedRow is not null)
        {
            selectedRow.Selected = true;
        }

        if (row?.Tag is not LyricsLine line || string.IsNullOrWhiteSpace(line.Explanation))
        {
            annotationTitle.Text = "No annotation";
            annotationTime.Text = string.Empty;
            annotationBody.Text = string.Empty;
            return;
        }

        annotationTitle.Text = line.Text;
        annotationTime.Text = FormatTimestamp(line.StartTime);
        annotationBody.Text = line.Explanation;
    }

    private void ScrollToCurrentLine()
    {
        var currentIndex = lyrics.FindLineIndex(positionSeconds);
        if (currentIndex >= 0 && currentIndex < rows.Count)
        {
            (lyricsPanel.Parent as ScrollableControl)?.ScrollControlIntoView(rows[currentIndex]);
        }
    }

    private string BuildSubtitle()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(track.Artist))
        {
            parts.Add(track.Artist);
        }

        if (!string.IsNullOrWhiteSpace(track.Album))
        {
            parts.Add(track.Album);
        }

        var annotatedCount = lyrics.Lines.Count(static line => !string.IsNullOrWhiteSpace(line.Explanation));
        parts.Add($"{annotatedCount} annotations");
        return string.Join("  /  ", parts);
    }

    private static string FormatTimestamp(double seconds)
    {
        var totalCentiseconds = (long)Math.Round(Math.Max(0, seconds) * 100d, MidpointRounding.AwayFromZero);
        var minutes = totalCentiseconds / 6000;
        var wholeSeconds = (totalCentiseconds / 100) % 60;
        var centiseconds = totalCentiseconds % 100;
        return $"{minutes:D2}:{wholeSeconds:D2}.{centiseconds:D2}";
    }

    private sealed class LyricsInspectorRow : Control
    {
        private readonly string timeText;
        private readonly string lyricText;
        private readonly ThemePalette palette;
        private readonly Font timeFont = new("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        private readonly Font lyricFont = new("Segoe UI Semibold", 13F, FontStyle.Bold, GraphicsUnit.Point);
        private bool selected;

        public LyricsInspectorRow(
            string timeText,
            string lyricText,
            bool hasAnnotation,
            bool isCurrent,
            ThemePalette palette)
        {
            this.timeText = timeText;
            this.lyricText = lyricText;
            this.palette = palette;
            HasAnnotation = hasAnnotation;
            IsCurrent = isCurrent;

            Cursor = hasAnnotation ? Cursors.Hand : Cursors.Default;
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
        }

        public bool HasAnnotation { get; }

        public bool IsCurrent { get; }

        public bool Selected
        {
            get => selected;
            set
            {
                selected = value;
                Invalidate();
            }
        }

        public void UpdatePreferredHeight(int width)
        {
            var textWidth = Math.Max(120, width - 134);
            var measured = TextRenderer.MeasureText(
                lyricText.Length == 0 ? " " : lyricText,
                lyricFont,
                new Size(textWidth, 2000),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            Height = Math.Max(48, measured.Height + 24);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rowBounds = new Rectangle(0, 0, Width - 1, Height - 1);
            var background = palette.SurfaceBackColor;
            if (HasAnnotation)
            {
                background = ThemePalette.Blend(palette.SurfaceBackColor, palette.AccentPrimaryColor, palette.IsDark ? 0.14f : 0.08f);
            }

            if (IsCurrent)
            {
                background = ThemePalette.Blend(background, palette.SurfaceRaisedColor, 0.45f);
            }

            if (Selected)
            {
                background = ThemePalette.Blend(palette.SurfaceRaisedColor, palette.AccentPrimaryColor, palette.IsDark ? 0.28f : 0.18f);
            }

            using (var brush = new SolidBrush(background))
            {
                FillRoundRect(graphics, brush, rowBounds, 8);
            }

            if (HasAnnotation)
            {
                using var accentBrush = new SolidBrush(palette.AccentPrimaryColor);
                FillRoundRect(graphics, accentBrush, new Rectangle(0, 0, 5, Height - 1), 3);
            }

            if (IsCurrent || Selected)
            {
                using var border = new Pen(ThemePalette.WithAlpha(palette.AccentPrimaryColor, Selected ? 190 : 105), 1F);
                DrawRoundRect(graphics, border, rowBounds, 8);
            }

            var timeColor = HasAnnotation ? palette.AccentPrimaryColor : palette.TextMutedColor;
            var textColor = HasAnnotation ? palette.TextPrimaryColor : palette.TextSecondaryColor;
            if (Selected)
            {
                textColor = palette.TextPrimaryColor;
            }

            var timeRect = new Rectangle(18, 14, 76, Height - 20);
            TextRenderer.DrawText(
                graphics,
                timeText,
                timeFont,
                timeRect,
                timeColor,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);

            var lyricRect = new Rectangle(104, 12, Math.Max(1, Width - 128), Height - 18);
            TextRenderer.DrawText(
                graphics,
                lyricText.Length == 0 ? " " : lyricText,
                lyricFont,
                lyricRect,
                textColor,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

            if (HasAnnotation)
            {
                var dotSize = 7;
                var dotRect = new Rectangle(Width - 22, 18, dotSize, dotSize);
                using var dotBrush = new SolidBrush(palette.AccentPrimaryColor);
                graphics.FillEllipse(dotBrush, dotRect);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timeFont.Dispose();
                lyricFont.Dispose();
            }

            base.Dispose(disposing);
        }

        private static void FillRoundRect(Graphics graphics, Brush brush, Rectangle rectangle, int radius)
        {
            using var path = CreateRoundRect(rectangle, radius);
            graphics.FillPath(brush, path);
        }

        private static void DrawRoundRect(Graphics graphics, Pen pen, Rectangle rectangle, int radius)
        {
            using var path = CreateRoundRect(rectangle, radius);
            graphics.DrawPath(pen, path);
        }

        private static GraphicsPath CreateRoundRect(Rectangle rectangle, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
