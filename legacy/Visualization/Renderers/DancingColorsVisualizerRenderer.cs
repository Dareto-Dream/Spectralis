using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

internal sealed class DancingColorsVisualizerRenderer : VisualizerRendererBase
{
    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        DrawBackground(graphics, bounds, scene);
        DrawDancingColors(graphics, bounds, scene);
        DrawHud(graphics, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(graphics, bounds, scene);
    }

    private static void DrawDancingColors(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var contentBounds = Rectangle.Inflate(bounds, -20, -24);
        var phase = scene.AnimationPhase * MathF.PI / 180f;
        var ribbonCount = 5;
        var sampleCount = Math.Clamp(contentBounds.Width / 9, 42, 96);
        var ribbonColors = new[]
        {
            scene.Theme.BarStartColor,
            Blend(scene.Theme.BarStartColor, scene.Theme.AmbientGlowColor, 0.55f),
            scene.Theme.BarEndColor,
            Blend(scene.Theme.BarEndColor, scene.Theme.HudLabelColor, 0.38f),
            Blend(scene.Theme.BarGlowColor, scene.Theme.PeakColor, 0.45f)
        };

        using var horizonPen = new Pen(Color.FromArgb(26, scene.Theme.AmbientGridColor), 1.2f);
        graphics.DrawLine(horizonPen, contentBounds.Left, contentBounds.Top + (contentBounds.Height * 0.58f), contentBounds.Right, contentBounds.Top + (contentBounds.Height * 0.58f));

        for (var ribbonIndex = 0; ribbonIndex < ribbonCount; ribbonIndex++)
        {
            var points = BuildRibbonPoints(contentBounds, scene, sampleCount, ribbonIndex, phase, ribbonCount);
            using var path = new GraphicsPath();
            path.AddCurve(points, 0.42f);

            var width = 28f - (ribbonIndex * 3.5f);
            using var glowPen = new Pen(Color.FromArgb(34, ribbonColors[ribbonIndex]), width)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            using var ribbonPen = new Pen(Color.FromArgb(185, ribbonColors[ribbonIndex]), Math.Max(3.2f, width * 0.24f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            graphics.DrawPath(glowPen, path);
            graphics.DrawPath(ribbonPen, path);

            DrawRibbonHighlights(graphics, points, ribbonColors[ribbonIndex], ribbonIndex);
        }
    }

    private static PointF[] BuildRibbonPoints(
        Rectangle contentBounds,
        VisualizerScene scene,
        int sampleCount,
        int ribbonIndex,
        float phase,
        int ribbonCount)
    {
        var points = new PointF[sampleCount];
        var baseY = contentBounds.Top + (contentBounds.Height * (0.28f + (ribbonIndex * 0.11f)));

        for (var sample = 0; sample < sampleCount; sample++)
        {
            var t = sample / (float)(sampleCount - 1);
            var spectrumIndex = Math.Clamp((int)(t * scene.SpectrumLevels.Length), 0, scene.SpectrumLevels.Length - 1);
            var waveformIndex = Math.Clamp((int)(t * (scene.WaveformPoints.Length - 1)), 0, scene.WaveformPoints.Length - 1);
            var energy = scene.SpectrumLevels[spectrumIndex];
            var wave = scene.WaveformPoints[waveformIndex];
            var shimmer = MathF.Sin((t * 7.5f) + phase + (ribbonIndex * 0.9f));
            var drift = MathF.Cos((t * 4.2f) - (phase * 2f) + (ribbonIndex * 0.7f));
            var amplitude = (contentBounds.Height * 0.08f) + (energy * contentBounds.Height * 0.16f);
            var y = baseY + (shimmer * amplitude) + (drift * amplitude * 0.35f) - (wave * contentBounds.Height * 0.18f);
            var x = contentBounds.Left + (t * contentBounds.Width);

            points[sample] = new PointF(x, y);
        }

        return points;
    }

    private static void DrawRibbonHighlights(Graphics graphics, PointF[] points, Color color, int ribbonIndex)
    {
        var step = Math.Max(8, 18 - (ribbonIndex * 2));
        using var highlightBrush = new SolidBrush(Color.FromArgb(86 - (ribbonIndex * 10), color));

        for (var index = ribbonIndex + 2; index < points.Length; index += step)
        {
            var size = Math.Max(4f, 10f - ribbonIndex);
            graphics.FillEllipse(
                highlightBrush,
                points[index].X - (size / 2f),
                points[index].Y - (size / 2f),
                size,
                size);
        }
    }

    private static Color Blend(Color start, Color end, float amount)
    {
        var mix = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(start.R + ((end.R - start.R) * mix)),
            (int)(start.G + ((end.G - start.G) * mix)),
            (int)(start.B + ((end.B - start.B) * mix)));
    }
}
