using System.Drawing.Drawing2D;

namespace Spectralis;

/// <summary>
/// Inline chip-based lyrics display for the timing studio.
/// Each word or syllable is rendered as a tappable chip; timed chips are filled accent,
/// the current-target chip has an accent border, untimed chips are muted.
/// Syllables of the same word are drawn with a 1 px gap; words have a 5 px gap;
/// lyric lines have an 18 px gap.
/// </summary>
internal sealed class LyricsTimingCanvas : Panel
{
    internal readonly struct Unit
    {
        public string Text     { get; init; }
        public bool IsTimed    { get; init; }
        public bool IsLineStart { get; init; }
        /// <summary>True for the first syllable of each word (and for every unit in word/line mode).</summary>
        public bool IsWordStart { get; init; }
    }

    private struct LayoutEntry
    {
        public Rectangle Rect;
        public string Text;
        public bool IsTimed;
        public bool IsLineStart;
        public bool IsWordStart;
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private LayoutEntry[] entries = [];
    private int _selectedIndex = -1;
    private ThemePalette? palette;
    private Font? itemFont;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired when the user single-clicks a chip (selection intent).</summary>
    public event EventHandler<int>? UnitSelected;

    // ── Constructor ───────────────────────────────────────────────────────────

    public LyricsTimingCanvas()
    {
        DoubleBuffered = true;
        AutoScroll = true;
    }

    // ── API ───────────────────────────────────────────────────────────────────

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value) return;
            var prev = _selectedIndex;
            _selectedIndex = value;
            InvalidateEntry(prev);
            InvalidateEntry(_selectedIndex);
        }
    }

    public void SetUnits(IReadOnlyList<Unit> units, ThemePalette p)
    {
        palette = p;
        itemFont ??= new Font("Segoe UI", 9.25F);

        entries = new LayoutEntry[units.Count];
        for (var i = 0; i < units.Count; i++)
        {
            entries[i] = new LayoutEntry
            {
                Text = units[i].Text,
                IsTimed = units[i].IsTimed,
                IsLineStart = units[i].IsLineStart,
                IsWordStart = units[i].IsWordStart
            };
        }

        _selectedIndex = entries.Length > 0 ? 0 : -1;
        AutoScrollPosition = Point.Empty;
        RecalculateLayout();
        Invalidate();
    }

    /// <summary>Marks a single entry timed/untimed and repaints just that chip.</summary>
    public void UpdateTimed(int index, bool isTimed)
    {
        if (index < 0 || index >= entries.Length) return;
        entries[index].IsTimed = isTimed;
        InvalidateEntry(index);
    }

    /// <summary>Selects an entry and scrolls it into view.</summary>
    public void SelectAndScroll(int index)
    {
        SelectedIndex = index;
        EnsureVisible(index);
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private void RecalculateLayout()
    {
        if (entries.Length == 0 || ClientSize.Width <= 0 || itemFont is null)
        {
            AutoScrollMinSize = Size.Empty;
            return;
        }

        const int xStart  = 12;
        const int yStart  = 14;
        const int chipH   = 28;
        const int wordGap = 5;
        const int sylGap  = 1;
        const int rowGap  = 8;
        const int lineGap = 18;
        const int hPad    = 10;

        var x    = xStart;
        var y    = yStart;
        var maxX = Math.Max(80, ClientSize.Width - xStart);

        for (var i = 0; i < entries.Length; i++)
        {
            ref var e = ref entries[i];

            if (e.IsLineStart && i > 0)
            {
                x = xStart;
                y += chipH + lineGap;
            }

            var textW = TextRenderer.MeasureText(
                e.Text, itemFont, new Size(2000, chipH), TextFormatFlags.NoPadding).Width;
            var chipW = Math.Max(20, textW + hPad * 2);

            // Wrap within a lyric line when running out of width.
            if (!e.IsLineStart && x + chipW > maxX && x > xStart)
            {
                x = xStart;
                y += chipH + rowGap;
            }

            e.Rect = new Rectangle(x, y, chipW, chipH);

            // Gap after this chip: depends on what the NEXT chip is.
            var gapAfter = (i + 1 < entries.Length && entries[i + 1].IsWordStart)
                ? wordGap
                : sylGap;
            x += chipW + gapAfter;
        }

        AutoScrollMinSize = new Size(0, y + chipH + yStart * 2);
    }

    private void EnsureVisible(int index)
    {
        if (index < 0 || index >= entries.Length) return;
        var r       = entries[index].Rect;
        var scrollY = -AutoScrollPosition.Y;
        var viewH   = ClientSize.Height;

        if (r.Top < scrollY)
            AutoScrollPosition = new Point(0, r.Top - 14);
        else if (r.Bottom > scrollY + viewH)
            AutoScrollPosition = new Point(0, r.Bottom - viewH + 14);
    }

    private void InvalidateEntry(int index)
    {
        if (index < 0 || index >= entries.Length) return;
        var r = entries[index].Rect;
        r.Offset(AutoScrollPosition);
        r.Inflate(3, 3);
        Invalidate(r);
    }

    // ── Paint ─────────────────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (entries.Length == 0 || palette is null || itemFont is null) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var offset = AutoScrollPosition;

        for (var i = 0; i < entries.Length; i++)
        {
            ref var entry = ref entries[i];
            var r = new Rectangle(
                entry.Rect.X + offset.X,
                entry.Rect.Y + offset.Y,
                entry.Rect.Width,
                entry.Rect.Height);

            if (!e.ClipRectangle.IntersectsWith(r)) continue;

            var isSelected = i == _selectedIndex;
            GetChipColors(entry.IsTimed, isSelected, palette,
                out var bgColor, out var fgColor, out var borderColor, out var borderW);

            using (var path = RoundedRect(r, 5))
            {
                if (bgColor.A > 0)
                {
                    using var brush = new SolidBrush(bgColor);
                    g.FillPath(brush, path);
                }
                if (borderW > 0)
                {
                    using var pen = new Pen(borderColor, borderW);
                    g.DrawPath(pen, path);
                }
            }

            TextRenderer.DrawText(g, entry.Text, itemFont, r, fgColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private static void GetChipColors(
        bool isTimed, bool isSelected, ThemePalette p,
        out Color bg, out Color fg, out Color border, out float borderW)
    {
        var accent = p.AccentPrimaryColor;
        borderW = 0;
        border  = Color.Transparent;

        if (isTimed && isSelected)
        {
            bg = accent;
            fg = Color.White;
        }
        else if (isTimed)
        {
            bg = Color.FromArgb(55, accent.R, accent.G, accent.B);
            fg = accent;
        }
        else if (isSelected)
        {
            bg     = Color.FromArgb(22, accent.R, accent.G, accent.B);
            fg     = p.TextPrimaryColor;
            border = accent;
            borderW = 1.5f;
        }
        else
        {
            var m = p.TextMutedColor;
            bg = Color.FromArgb(18, m.R, m.G, m.B);
            fg = p.TextMutedColor;
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var pt = new Point(e.X - AutoScrollPosition.X, e.Y - AutoScrollPosition.Y);

        for (var i = 0; i < entries.Length; i++)
        {
            if (!entries[i].Rect.Contains(pt)) continue;
            SelectedIndex = i;
            UnitSelected?.Invoke(this, i);
            Focus();
            return;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RecalculateLayout();
        Invalidate();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GraphicsPath RoundedRect(Rectangle r, float radius)
    {
        var d    = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left,          r.Top,           d, d, 180, 90);
        path.AddArc(r.Right - d,     r.Top,           d, d, 270, 90);
        path.AddArc(r.Right - d,     r.Bottom - d,    d, d, 0,   90);
        path.AddArc(r.Left,          r.Bottom - d,    d, d, 90,  90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            itemFont?.Dispose();
        base.Dispose(disposing);
    }
}
