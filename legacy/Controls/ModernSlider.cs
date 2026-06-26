using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Spectralis;

/// <summary>
/// Custom-drawn horizontal slider that replaces the system TrackBar.
/// Used for seeking, volume, and sensitivity controls.
/// </summary>
public sealed class ModernSlider : Control
{
    private int minimum;
    private int maximum = 100;
    private int currentValue;
    private Color trackColor = Color.FromArgb(112, 86, 71, 62);
    private Color accentStartColor = Color.FromArgb(246, 186, 96);
    private Color accentEndColor = Color.FromArgb(221, 108, 79);
    private Color focusColor = Color.FromArgb(255, 216, 164, 98);
    private bool isDragging;
    private bool isHovering;
    private float hoverAlpha;
    private readonly System.Windows.Forms.Timer animTimer;

    public event EventHandler? Scroll;

    public ModernSlider()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);

        Cursor = Cursors.Hand;
        TabStop = true;
        AutoSize = false;

        animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        animTimer.Tick += OnAnimTick;
    }

    /// <summary>When true, renders as a prominent seek bar with a larger thumb.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsLarge { get; set; } = false;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum
    {
        get => minimum;
        set { minimum = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => maximum;
        set { maximum = Math.Max(minimum + 1, value); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => currentValue;
        set
        {
            currentValue = Math.Clamp(value, minimum, maximum);
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color TrackColor
    {
        get => trackColor;
        set { trackColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color AccentStartColor
    {
        get => accentStartColor;
        set { accentStartColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color AccentEndColor
    {
        get => accentEndColor;
        set { accentEndColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FocusColor
    {
        get => focusColor;
        set { focusColor = value; Invalidate(); }
    }

    // Expose TickStyle as a no-op for Designer compatibility
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public System.Windows.Forms.TickStyle TickStyle { get; set; } = System.Windows.Forms.TickStyle.None;

    private void OnAnimTick(object? sender, EventArgs e)
    {
        var target = (isHovering || isDragging) ? 1f : 0f;
        hoverAlpha += (target - hoverAlpha) * 0.22f;
        if (Math.Abs(hoverAlpha - target) < 0.005f)
        {
            hoverAlpha = target;
            if (!isHovering && !isDragging)
                animTimer.Stop();
        }

        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        isHovering = true;
        animTimer.Start();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        isHovering = false;
        if (!isDragging) animTimer.Start();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Enabled)
        {
            isDragging = true;
            Focus();
            SetValueFromX(e.X);
            Scroll?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (isDragging)
        {
            SetValueFromX(e.X);
            Scroll?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            isDragging = false;
            animTimer.Start();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = Math.Max(1, (maximum - minimum) / 20);
        switch (e.KeyCode)
        {
            case Keys.Left:
            case Keys.Down:
                Value = Math.Max(minimum, currentValue - step);
                Scroll?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Keys.Right:
            case Keys.Up:
                Value = Math.Min(maximum, currentValue + step);
                Scroll?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Keys.Home:
                Value = minimum;
                Scroll?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Keys.End:
                Value = maximum;
                Scroll?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    private void SetValueFromX(int mouseX)
    {
        var pad = GetPad();
        var trackWidth = Width - pad * 2;
        if (trackWidth <= 0) return;

        var fraction = Math.Clamp((mouseX - pad) / (float)trackWidth, 0f, 1f);
        currentValue = (int)Math.Round(minimum + fraction * (maximum - minimum));
        Invalidate();
    }

    private int GetPad() => IsLarge ? 10 : 6;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var h = Enabled ? hoverAlpha : 0f;
        var alphaScale = Enabled ? 1f : 0.5f;
        var pad = GetPad();
        var trackLeft = pad;
        var trackRight = Width - pad;
        var trackWidth = trackRight - trackLeft;
        var centerY = Height / 2f;

        if (trackWidth <= 0)
            return;

        var baseTrackH = IsLarge ? 4f : 3.5f;
        var activeTrackH = IsLarge ? 8f : 6f;
        var trackH = baseTrackH + ((activeTrackH - baseTrackH) * h);

        var baseThumb = IsLarge ? 14 : 12;
        var hoverThumb = IsLarge ? 22 : 17;
        var thumbSize = (int)Math.Round(baseThumb + ((hoverThumb - baseThumb) * h));

        var fraction = maximum > minimum
            ? (currentValue - minimum) / (float)(maximum - minimum)
            : 0f;
        var thumbX = trackLeft + (trackWidth * fraction);
        var trackY = centerY - (trackH / 2f);
        var trackRect = new RectangleF(trackLeft, trackY, trackWidth, trackH);

        using (var trackBrush = new SolidBrush(ApplyOpacity(trackColor, alphaScale)))
        {
            DrawRoundedRect(g, trackRect, trackH / 2f, trackBrush);
        }

        var filledW = Math.Max(trackH, trackWidth * fraction);
        if (fraction > 0f)
        {
            using var fillBrush = new LinearGradientBrush(
                new PointF(trackLeft, 0),
                new PointF(trackLeft + filledW + 1, 0),
                ApplyOpacity(accentStartColor, alphaScale),
                ApplyOpacity(accentEndColor, alphaScale));
            DrawRoundedRect(
                g,
                new RectangleF(trackLeft, trackY, Math.Min(trackWidth, filledW), trackH),
                trackH / 2f,
                fillBrush);
        }

        if (Focused)
        {
            var focusBounds = new RectangleF(1.5f, 1.5f, Width - 3f, Height - 3f);
            using var focusPath = CreateRoundedPath(focusBounds, Math.Min(10f, Height / 2f));
            using var focusPen = new Pen(ApplyOpacity(focusColor, 0.55f), 1.3f);
            g.DrawPath(focusPen, focusPath);
        }

        var thumbRect = new RectangleF(
            thumbX - (thumbSize / 2f),
            centerY - (thumbSize / 2f),
            thumbSize,
            thumbSize);

        using (var shadowBrush = new SolidBrush(ApplyOpacity(Color.FromArgb(40, 0, 0, 0), alphaScale)))
        {
            var shadowRect = thumbRect;
            shadowRect.Offset(0, 1f);
            g.FillEllipse(shadowBrush, shadowRect);
        }

        if (h > 0.01f)
        {
            using var glowBrush = new SolidBrush(ApplyOpacity(accentStartColor, h * 35f / 255f * alphaScale));
            var glowRect = RectangleF.Inflate(thumbRect, 5f + h, 5f + h);
            g.FillEllipse(glowBrush, glowRect);
        }

        using (var thumbBrush = new SolidBrush(ApplyOpacity(accentStartColor, alphaScale)))
            g.FillEllipse(thumbBrush, thumbRect);

        using (var thumbBorderPen = new Pen(ApplyOpacity(accentEndColor, (60f + (h * 50f)) / 255f * alphaScale), 1f))
            g.DrawEllipse(thumbBorderPen, thumbRect);
    }

    private static void DrawRoundedRect(Graphics g, RectangleF rect, float radius, Brush brush)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        using var path = CreateRoundedPath(rect, radius);
        g.FillPath(brush, path);
    }

    private static GraphicsPath CreateRoundedPath(RectangleF rect, float radius)
    {
        var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f);
        var path = new GraphicsPath();
        if (r < 0.5f)
        {
            path.AddRectangle(rect);
            return path;
        }

        var d = r * 2f;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) animTimer.Dispose();
        base.Dispose(disposing);
    }

    private static Color ApplyOpacity(Color color, float opacity)
    {
        var alpha = (int)Math.Round(Math.Clamp(opacity, 0f, 1f) * 255f);
        return Color.FromArgb(alpha, color);
    }
}
