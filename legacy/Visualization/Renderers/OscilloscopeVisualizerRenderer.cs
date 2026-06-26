using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

// Phosphor-glow oscilloscope — renders the waveform as a bright scan line with a
// CRT-style outer glow and dot markers, all colored from the active theme accent.
internal sealed class OscilloscopeVisualizerRenderer : VisualizerRendererBase
{
    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        DrawBackground(graphics, bounds, scene);
        DrawOscilloscope(graphics, bounds, scene);
        DrawHud(graphics, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(graphics, bounds, scene);
    }

    private static void DrawOscilloscope(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var contentBounds = Rectangle.Inflate(bounds, -18, -28);
        var centerY = contentBounds.Top + (contentBounds.Height / 2f);
        var halfHeight = contentBounds.Height * 0.44f;

        // Center axis
        using var axisPen = new Pen(Color.FromArgb(28, scene.Theme.HudLabelColor), 1f);
        graphics.DrawLine(axisPen, contentBounds.Left, centerY, contentBounds.Right, centerY);

        if (scene.WaveformPoints.Length < 2)
            return;

        using var outerGlowPen = new Pen(Color.FromArgb(18, scene.Theme.AmbientGlowColor), 18f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var innerGlowPen = new Pen(Color.FromArgb(58, scene.Theme.BarGlowColor), 6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var linePen = new Pen(Color.FromArgb(228, scene.Theme.BarStartColor), 2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        using var path = BuildPath(contentBounds, centerY, halfHeight, scene.WaveformPoints);
        graphics.DrawPath(outerGlowPen, path);
        graphics.DrawPath(innerGlowPen, path);
        graphics.DrawPath(linePen, path);

        // Phosphor dot markers along the trace
        using var dotBrush = new SolidBrush(Color.FromArgb(175, scene.Theme.BarEndColor));
        var dotStep = Math.Max(3, scene.WaveformPoints.Length / 90);
        for (var index = 0; index < scene.WaveformPoints.Length; index += dotStep)
        {
            var x = contentBounds.Left + (index / (float)(scene.WaveformPoints.Length - 1)) * contentBounds.Width;
            var y = centerY - (scene.WaveformPoints[index] * halfHeight);
            graphics.FillEllipse(dotBrush, x - 2f, y - 2f, 4f, 4f);
        }
    }

    private static GraphicsPath BuildPath(Rectangle bounds, float centerY, float halfHeight, float[] points)
    {
        var path = new GraphicsPath();
        var pts = new PointF[points.Length];
        for (var index = 0; index < points.Length; index++)
        {
            var x = bounds.Left + (index / (float)(points.Length - 1)) * bounds.Width;
            pts[index] = new PointF(x, centerY - (points[index] * halfHeight));
        }

        if (pts.Length > 1)
            path.AddCurve(pts, 0.28f);

        return path;
    }
}
