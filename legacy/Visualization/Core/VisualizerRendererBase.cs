using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

internal abstract class VisualizerRendererBase : IVisualizerRenderer
{
    public abstract void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene);

    protected static void DrawBackground(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var glowStrength = Math.Clamp((scene.PeakLevel * 0.45f) + (scene.RmsLevel * 0.95f), 0.05f, 0.75f);

        using var backgroundBrush = new LinearGradientBrush(
            bounds,
            scene.Theme.BackgroundTopColor,
            scene.Theme.BackgroundBottomColor,
            LinearGradientMode.Vertical);
        graphics.FillRectangle(backgroundBrush, bounds);

        using var accentBrush = new PathGradientBrush(
            new Point[]
            {
                new Point(bounds.Width / 2, bounds.Height / 2),
                new Point(bounds.Right, bounds.Height / 3),
                new Point(bounds.Right - 20, bounds.Bottom - 20),
                new Point(bounds.Left + 20, bounds.Bottom - 10),
                new Point(bounds.Left, bounds.Height / 3)
            })
        {
            CenterColor = Color.FromArgb((int)(92 * glowStrength), scene.Theme.AmbientGlowColor),
            SurroundColors =
            [
                Color.FromArgb(0, scene.Theme.AmbientGlowColor),
                Color.FromArgb(0, scene.Theme.AmbientGlowColor),
                Color.FromArgb(0, scene.Theme.AmbientGlowColor),
                Color.FromArgb(0, scene.Theme.AmbientGlowColor),
                Color.FromArgb(0, scene.Theme.AmbientGlowColor)
            ]
        };
        graphics.FillRectangle(accentBrush, bounds);
    }

    protected static void DrawGrid(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        using var horizontalPen = new Pen(Color.FromArgb(22, scene.Theme.AmbientGridColor));
        using var verticalPen = new Pen(Color.FromArgb(14, scene.Theme.AmbientGridColor));

        for (var index = 1; index < 5; index++)
        {
            var y = bounds.Top + (index * bounds.Height / 5);
            graphics.DrawLine(horizontalPen, bounds.Left, y, bounds.Right, y);
        }

        for (var index = 1; index < 10; index++)
        {
            var x = bounds.Left + (index * bounds.Width / 10);
            graphics.DrawLine(verticalPen, x, bounds.Top, x, bounds.Bottom);
        }
    }

    protected static void DrawHud(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var hudBounds = new Rectangle(bounds.Left + 18, bounds.Top + 14, bounds.Width - 36, 40);
        var meterWidth = 120;
        var meterHeight = 8;
        var meterRect = new Rectangle(hudBounds.Right - meterWidth, hudBounds.Top + 12, meterWidth, meterHeight);

        using var labelBrush = new SolidBrush(scene.Theme.HudLabelColor);
        using var infoBrush = new SolidBrush(scene.Theme.HudInfoColor);
        using var meterBackBrush = new SolidBrush(Color.FromArgb(42, 255, 245, 230));
        using var meterFillBrush = new LinearGradientBrush(
            meterRect,
            scene.Theme.BarStartColor,
            scene.Theme.BarEndColor,
            LinearGradientMode.Horizontal);

        graphics.DrawString(scene.ModeLabel, scene.Font, labelBrush, hudBounds.Left, hudBounds.Top);
        graphics.DrawString(
            scene.IsActive ? "Live" : "Idle",
            scene.Font,
            infoBrush,
            hudBounds.Left,
            hudBounds.Top + 18);

        graphics.FillRectangle(meterBackBrush, meterRect);
        graphics.FillRectangle(
            meterFillBrush,
            meterRect.Left,
            meterRect.Top,
            Math.Max(6, (int)(meterRect.Width * Math.Clamp(scene.PeakLevel, 0, 1))),
            meterRect.Height);
        graphics.DrawString(
            $"Peak {(int)(Math.Clamp(scene.PeakLevel, 0, 1) * 100)}%",
            scene.Font,
            infoBrush,
            meterRect.Left - 64,
            meterRect.Top - 6);
    }

    protected static void DrawPlaceholder(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        using var textBrush = new SolidBrush(scene.Theme.PlaceholderColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        graphics.DrawString("Drop audio here or use Open to start playback", scene.Font, textBrush, bounds, format);
    }

    protected static bool IsNearSilence(VisualizerScene scene) =>
        scene.SpectrumLevels.All(level => level < 0.01f) &&
        scene.WaveformPoints.All(point => Math.Abs(point) < 0.01f);

    protected static float SampleRange(float[] source, int index, int displayBars)
    {
        var start = index * source.Length / displayBars;
        var end = Math.Max(start + 1, ((index + 1) * source.Length) / displayBars);
        float total = 0;

        for (var position = start; position < end; position++)
            total += source[position];

        return total / (end - start);
    }

    protected static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var safeRadius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);
        var diameter = Math.Max(2, safeRadius * 2);
        var path = new GraphicsPath();

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();

        return path;
    }
}
