using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Spectralis;

/// <summary>
/// Modern toggle switch control with smooth animation.
/// </summary>
public sealed class ModernSwitch : Control
{
    private bool isChecked;
    private Color onColor = Color.FromArgb(92, 163, 255);
    private Color offColor = Color.FromArgb(87, 71, 61);
    private Color thumbColor = Color.FromArgb(255, 255, 255);
    private bool isHovering;
    private float togglePosition; // 0 = off, 1 = on
    private readonly System.Windows.Forms.Timer animTimer;
    private const int TrackHeight = 24;
    private const int TrackWidth = 48;
    private const int ThumbRadius = 10;

    public ModernSwitch()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);

        Cursor = Cursors.Hand;
        TabStop = true;
        Size = new Size(TrackWidth, TrackHeight);
        DoubleBuffered = true;

        animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        animTimer.Tick += OnAnimTick;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => isChecked;
        set
        {
            if (isChecked != value)
            {
                isChecked = value;
                animTimer.Start();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color OnColor
    {
        get => onColor;
        set { onColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color OffColor
    {
        get => offColor;
        set { offColor = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ThumbColor
    {
        get => thumbColor;
        set { thumbColor = value; Invalidate(); }
    }

    public event EventHandler? CheckedChanged;

    protected override void OnMouseEnter(EventArgs e)
    {
        isHovering = true;
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        isHovering = false;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
        {
            Checked = !Checked;
        }
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Return)
        {
            Checked = !Checked;
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Color.Transparent);

        var centerY = Height / 2;
        var trackLeft = 0f;
        var trackTop = centerY - TrackHeight / 2;

        // Draw track
        var trackRect = new RectangleF(trackLeft, trackTop, TrackWidth, TrackHeight);
        var trackColor = isChecked ? onColor : offColor;
        if (isHovering)
        {
            trackColor = isChecked
                ? ThemePalette.Blend(onColor, Color.White, 0.15f)
                : ThemePalette.Blend(offColor, Color.White, 0.15f);
        }

        using (var brush = new SolidBrush(trackColor))
        {
            e.Graphics.FillRoundedRectangle(brush, trackRect, TrackHeight / 2);
        }

        // Draw thumb
        var thumbX = trackLeft + (TrackWidth - ThumbRadius * 2) * togglePosition + ThumbRadius;
        var thumbY = centerY;
        e.Graphics.FillEllipse(new SolidBrush(thumbColor), thumbX - ThumbRadius, thumbY - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);

        // Draw focus indicator if focused
        if (Focused)
        {
            using (var pen = new Pen(onColor, 1.5f))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        var target = isChecked ? 1f : 0f;
        togglePosition += (target - togglePosition) * 0.25f;

        if (Math.Abs(togglePosition - target) < 0.01f)
        {
            togglePosition = target;
            animTimer.Stop();
        }

        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            animTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Extension to fill rounded rectangles.
/// </summary>
internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF rect, float radius)
    {
        using (var path = new GraphicsPath())
        {
            path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
            path.AddArc(rect.X + rect.Width - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
            path.AddArc(rect.X + rect.Width - radius * 2, rect.Y + rect.Height - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(rect.X, rect.Y + rect.Height - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
        }
    }
}
