using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

// Filled spectrum area — smooth curve fitted to the frequency bins, filled with a
// vertical gradient and outlined with a glowing accent line. A secondary peak-hold
// curve floats above the filled area when peak hold is enabled.
internal sealed class SpectrumWaveVisualizerRenderer : VisualizerRendererBase
{
    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        DrawBackground(graphics, bounds, scene);
        DrawGrid(graphics, bounds, scene);
        DrawSpectrumWave(graphics, bounds, scene);
        DrawHud(graphics, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(graphics, bounds, scene);
    }

    private static void DrawSpectrumWave(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var cb = Rectangle.Inflate(bounds, -18, -18);
        var displayPoints = Math.Clamp(cb.Width / 6, 24, scene.SpectrumLevels.Length);
        var bottom = (float)cb.Bottom;

        var topPoints = BuildTopPoints(cb, bottom, scene.SpectrumLevels, displayPoints);

        // Filled area
        using var fillPath = BuildFillPath(topPoints, cb.Left, cb.Right, bottom);
        using var fillBrush = new LinearGradientBrush(
            new Rectangle(cb.Left, cb.Top, cb.Width, cb.Height),
            Color.FromArgb(125, scene.Theme.BarStartColor),
            Color.FromArgb(28,  scene.Theme.BarEndColor),
            LinearGradientMode.Vertical);
        graphics.FillPath(fillBrush, fillPath);

        // Outline glow + line
        using var curvePath = BuildCurvePath(topPoints);
        using var glowPen = new Pen(Color.FromArgb(65, scene.Theme.AmbientGlowColor), 9f) { LineJoin = LineJoin.Round };
        using var linePen = new Pen(Color.FromArgb(218, scene.Theme.BarStartColor), 2.5f) { LineJoin = LineJoin.Round };
        graphics.DrawPath(glowPen, curvePath);
        graphics.DrawPath(linePen, curvePath);

        // Peak-hold curve overlay
        if (!scene.ShowPeaks)
            return;

        var peakPoints = BuildTopPoints(cb, bottom, scene.PeakHoldLevels, displayPoints);
        using var peakPath = BuildCurvePath(peakPoints);
        using var peakPen = new Pen(Color.FromArgb(128, scene.Theme.PeakColor), 1.5f) { LineJoin = LineJoin.Round };
        graphics.DrawPath(peakPen, peakPath);
    }

    private static PointF[] BuildTopPoints(Rectangle cb, float bottom, float[] levels, int count)
    {
        var pts = new PointF[count];
        for (var index = 0; index < count; index++)
        {
            var level = SampleRange(levels, index, count);
            var x = cb.Left + (index / (float)(count - 1)) * cb.Width;
            var y = bottom - Math.Max(4f, cb.Height * level);
            pts[index] = new PointF(x, y);
        }
        return pts;
    }

    private static GraphicsPath BuildFillPath(PointF[] topPoints, float left, float right, float bottom)
    {
        var path = new GraphicsPath();
        path.AddLine(left, bottom, topPoints[0].X, topPoints[0].Y);
        if (topPoints.Length > 1)
            path.AddCurve(topPoints, 0.40f);
        path.AddLine(topPoints[^1].X, topPoints[^1].Y, right, bottom);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath BuildCurvePath(PointF[] topPoints)
    {
        var path = new GraphicsPath();
        if (topPoints.Length > 1)
            path.AddCurve(topPoints, 0.40f);
        return path;
    }
}
