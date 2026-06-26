using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

internal sealed class WaveformVisualizerRenderer : VisualizerRendererBase
{
    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        DrawBackground(graphics, bounds, scene);
        DrawGrid(graphics, bounds, scene);
        DrawWaveform(graphics, bounds, scene);
        DrawHud(graphics, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(graphics, bounds, scene);
    }

    private static void DrawWaveform(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var contentBounds = Rectangle.Inflate(bounds, -18, -24);
        var centerY = contentBounds.Top + (contentBounds.Height / 2f);

        using var glowPen = new Pen(Color.FromArgb(40, scene.Theme.AmbientGlowColor), 8)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var wavePen = new Pen(Color.FromArgb(240, scene.Theme.BarStartColor), 3)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var centerPen = new Pen(Color.FromArgb(72, scene.Theme.HudLabelColor), 1.5f);

        graphics.DrawLine(centerPen, contentBounds.Left, centerY, contentBounds.Right, centerY);

        using var wavePath = BuildWaveformPath(contentBounds, centerY, scene.WaveformPoints);
        graphics.DrawPath(glowPen, wavePath);
        graphics.DrawPath(wavePen, wavePath);
    }

    private static GraphicsPath BuildWaveformPath(Rectangle contentBounds, float centerY, float[] waveformPoints)
    {
        var path = new GraphicsPath();
        var points = new PointF[waveformPoints.Length];

        for (var index = 0; index < waveformPoints.Length; index++)
        {
            var x = contentBounds.Left + (index / (float)(waveformPoints.Length - 1) * contentBounds.Width);
            var y = centerY - (waveformPoints[index] * (contentBounds.Height * 0.45f));
            points[index] = new PointF(x, y);
        }

        if (points.Length > 1)
            path.AddCurve(points, 0.35f);

        return path;
    }
}
