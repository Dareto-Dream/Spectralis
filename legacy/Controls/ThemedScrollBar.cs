using System.Drawing.Drawing2D;

namespace Spectralis;

internal sealed class ThemedScrollBar : Control
{
    private int minimum;
    private int maximum = 100;
    private int largeChange = 10;
    private int currentValue;
    private Color thumbColor = Color.FromArgb(65, 160, 160, 160);
    private Color thumbActiveColor = Color.FromArgb(135, 210, 210, 210);

    private bool isDragging;
    private bool isHovering;
    private float dragStartY;
    private int dragStartValue;

    public event EventHandler? Scroll;

    public ThemedScrollBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);
        Width = 10;
        Cursor = Cursors.Default;
    }

    public int Minimum
    {
        get => minimum;
        set { minimum = value; Invalidate(); }
    }

    public int Maximum
    {
        get => maximum;
        set { maximum = Math.Max(minimum, value); Invalidate(); }
    }

    public int LargeChange
    {
        get => largeChange;
        set { largeChange = Math.Max(1, value); Invalidate(); }
    }

    public int Value
    {
        get => currentValue;
        set
        {
            var clamped = Math.Clamp(value, minimum, Math.Max(minimum, maximum - largeChange));
            if (currentValue == clamped) return;
            currentValue = clamped;
            Invalidate();
        }
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.WindowBackColor;
        thumbColor = ThemePalette.WithAlpha(palette.TextMutedColor, 65);
        thumbActiveColor = ThemePalette.WithAlpha(palette.TextSoftColor, 135);
        Invalidate();
    }

    private (float Top, float Height) GetThumbBounds()
    {
        var scrollRange = maximum - minimum;
        if (scrollRange <= 0 || largeChange >= scrollRange)
            return (0f, Height);

        var thumbRatio = (float)largeChange / scrollRange;
        var thumbH = Math.Max(32f, Height * thumbRatio);
        var scrollableH = Height - thumbH;
        var scrollableRange = scrollRange - largeChange;
        var thumbTop = scrollableRange > 0
            ? (currentValue - minimum) / (float)scrollableRange * scrollableH
            : 0f;
        return (thumbTop, thumbH);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); isHovering = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); isHovering = false; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        var (thumbTop, thumbH) = GetThumbBounds();
        if (e.Y >= thumbTop && e.Y <= thumbTop + thumbH)
        {
            isDragging = true;
            dragStartY = e.Y;
            dragStartValue = currentValue;
            Capture = true;
        }
        else
        {
            var scrollableRange = maximum - minimum - largeChange;
            if (scrollableRange > 0)
            {
                var (_, tH) = GetThumbBounds();
                var fraction = Math.Clamp((e.Y - tH / 2f) / (Height - tH), 0f, 1f);
                Value = minimum + (int)(fraction * scrollableRange);
                Scroll?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!isDragging) return;

        var (_, thumbH) = GetThumbBounds();
        var trackRange = Height - thumbH;
        if (trackRange <= 0) return;

        var scrollableRange = maximum - minimum - largeChange;
        var delta = (e.Y - dragStartY) / trackRange * scrollableRange;
        Value = dragStartValue + (int)delta;
        Scroll?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            isDragging = false;
            Capture = false;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        if (maximum - minimum <= largeChange) return;

        var (thumbTop, thumbH) = GetThumbBounds();
        var active = isHovering || isDragging;
        var thumbW = active ? Width - 2f : Width - 4f;
        var thumbX = (Width - thumbW) / 2f;
        var thumbRect = new RectangleF(thumbX, thumbTop + 3f, thumbW, thumbH - 6f);
        if (thumbRect.Height < 6f) return;

        using var thumbPath = RoundedPath(thumbRect, thumbRect.Width / 2f);
        using var brush = new SolidBrush(active ? thumbActiveColor : thumbColor);
        g.FillPath(brush, thumbPath);
    }

    private static GraphicsPath RoundedPath(RectangleF b, float r)
    {
        var path = new GraphicsPath();
        r = Math.Min(r, Math.Min(b.Width, b.Height) / 2f);
        if (r < 0.5f) { path.AddRectangle(b); return path; }
        var d = r * 2f;
        path.AddArc(b.Left, b.Top, d, d, 180, 90);
        path.AddArc(b.Right - d, b.Top, d, d, 270, 90);
        path.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90);
        path.AddArc(b.Left, b.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
