using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Spectralis;

/// <summary>
/// Custom-drawn button. Supports solid, ghost, and pill variants.
/// </summary>
public sealed class ModernButton : Control
{
    private Color accentColor = Color.FromArgb(50, 74, 128);
    private Color surfaceColor = Color.FromArgb(31, 27, 33);
    private Color disabledTextBlendColor = Color.FromArgb(98, 92, 88);
    private bool isHovering;
    private bool isPressed;
    private float hoverAlpha;
    private float cornerRadius;
    private readonly System.Windows.Forms.Timer animTimer;

    public ModernButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        // Disable built-in click so base.OnMouseUp doesn't fire Click;
        // our custom OnMouseUp handles it exclusively.
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);

        Cursor = Cursors.Hand;
        TabStop = true;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        ForeColor = Color.FromArgb(210, 225, 255);

        animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        animTimer.Tick += OnAnimTick;
    }

    /// <summary>Background fill color (ignored when IsGhost = true).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color AccentColor
    {
        get => accentColor;
        set { accentColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceColor
    {
        get => surfaceColor;
        set { surfaceColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color DisabledTextBlendColor
    {
        get => disabledTextBlendColor;
        set { disabledTextBlendColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float CornerRadius
    {
        get => cornerRadius;
        set { cornerRadius = Math.Max(0f, value); Invalidate(); }
    }

    /// <summary>When true, renders as a fully-rounded pill shape.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Pill { get; set; } = false;

    /// <summary>
    /// When true, renders transparent with a subtle border instead of a filled background.
    /// On hover a very light tint of AccentColor is shown.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsGhost { get; set; } = false;

    private void OnAnimTick(object? sender, EventArgs e)
    {
        var target = isHovering ? 1f : 0f;
        hoverAlpha += (target - hoverAlpha) * 0.25f;
        if (Math.Abs(hoverAlpha - target) < 0.005f)
        {
            hoverAlpha = target;
            if (!isHovering) animTimer.Stop();
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
        animTimer.Start();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && Enabled)
        {
            isPressed = true;
            Focus();
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left)
        {
            var wasPressed = isPressed;
            isPressed = false;
            Invalidate();
            if (wasPressed && ClientRectangle.Contains(e.Location))
                OnClick(EventArgs.Empty);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && Enabled)
        {
            isPressed = true;
            Invalidate();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
        {
            isPressed = false;
            Invalidate();
            if (Enabled) OnClick(EventArgs.Empty);
            e.Handled = true;
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var h = Enabled ? hoverAlpha : 0f;
        var alphaScale = Enabled ? 1f : 0.55f;
        var pressShift = isPressed ? 1f : 0f;
        var bounds = new RectangleF(0.75f, 0.75f + pressShift, Width - 1.5f, Height - 3f);
        var radius = cornerRadius > 0f
            ? cornerRadius
            : Pill
                ? Math.Max(4.5f, Math.Min(8f, bounds.Height * 0.20f))
                : Math.Min(7f, Math.Max(4f, bounds.Height * 0.16f));

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        using var path = CreateRoundedPath(bounds, radius);

        var shadowBounds = bounds;
        shadowBounds.Offset(0, isPressed ? 0f : 0.8f);
        using var shadowPath = CreateRoundedPath(shadowBounds, radius);
        var shadowBase = Blend(accentColor, Color.Black, 0.75f);
        var shadowAlpha = IsGhost ? 6f + (h * 6f) : 10f + (h * 8f);
        using (var shadowBrush = new SolidBrush(ApplyOpacity(shadowBase, shadowAlpha / 255f * alphaScale)))
            g.FillPath(shadowBrush, shadowPath);

        if (IsGhost)
        {
            var fillAlpha = (6f + (h * 24f) + (isPressed ? 12f : 0f)) / 255f;
            if (fillAlpha > 0.01f)
            {
                var ghostBase = Blend(surfaceColor, accentColor, 0.14f + (h * 0.10f));
                using var tintBrush = new SolidBrush(ApplyOpacity(ghostBase, fillAlpha * alphaScale));
                g.FillPath(tintBrush, path);
            }

            var borderBase = Blend(accentColor, Color.White, 0.18f);
            using var borderPen = new Pen(ApplyOpacity(borderBase, (70f + (h * 58f)) / 255f * alphaScale), 1f);
            g.DrawPath(borderPen, path);
        }
        else
        {
            var fillColor = isPressed
                ? Blend(accentColor, Color.Black, 0.10f)
                : Blend(accentColor, Color.White, h * 0.08f);
            using var fillBrush = new SolidBrush(ApplyOpacity(fillColor, alphaScale));
            g.FillPath(fillBrush, path);

            var borderBase = Blend(accentColor, Color.White, 0.20f);
            using var borderPen = new Pen(ApplyOpacity(borderBase, (26f + (h * 12f)) / 255f * alphaScale), 1f);
            g.DrawPath(borderPen, path);
        }

        if (Focused)
        {
            var focusBounds = new RectangleF(bounds.Left - 2.5f, bounds.Top - 2.5f, bounds.Width + 5f, bounds.Height + 5f);
            using var focusPath = CreateRoundedPath(focusBounds, radius + 2.5f);
            using var focusPen = new Pen(ApplyOpacity(Blend(accentColor, Color.White, 0.36f), 0.65f), 1.15f);
            g.DrawPath(focusPen, focusPath);
        }

        var textColor = Enabled
            ? ForeColor
            : Blend(ForeColor, disabledTextBlendColor, 0.35f);
        var textRect = new RectangleF(bounds.Left, bounds.Top - 0.5f, bounds.Width, bounds.Height);

        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };

        var textAlpha = Enabled ? (IsGhost ? 210f + (h * 36f) : 245f) : 135f;
        using var textBrush = new SolidBrush(ApplyOpacity(textColor, textAlpha / 255f));
        g.DrawString(Text, Font, textBrush, textRect, sf);
    }

    private static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var r = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2f);
        if (r < 0.5f)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var d = r * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
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

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            255,
            (int)Math.Round(from.R + ((to.R - from.R) * amount)),
            (int)Math.Round(from.G + ((to.G - from.G) * amount)),
            (int)Math.Round(from.B + ((to.B - from.B) * amount)));
    }
}
