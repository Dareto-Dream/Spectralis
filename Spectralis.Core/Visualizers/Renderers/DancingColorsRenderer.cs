using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

public sealed class DancingColorsRenderer : VisualizerRendererBase
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);
        DrawDancingColors(canvas, bounds, scene);
        DrawHud(canvas, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(canvas, bounds, scene);
    }

    private static void DrawDancingColors(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var contentBounds = bounds.Inflate(-20, -24);
        var phase = scene.AnimationPhase * MathF.PI / 180f;
        const int ribbonCount = 5;
        var sampleCount = Math.Clamp((int)(contentBounds.Width / 9), 42, 96);
        var ribbonColors = new[]
        {
            scene.Theme.BarStartColor,
            VizColor.Blend(scene.Theme.BarStartColor, scene.Theme.AmbientGlowColor, 0.55f),
            scene.Theme.BarEndColor,
            VizColor.Blend(scene.Theme.BarEndColor, scene.Theme.HudLabelColor, 0.38f),
            VizColor.Blend(scene.Theme.BarGlowColor, scene.Theme.PeakColor, 0.45f),
        };

        var horizonY = contentBounds.Top + (contentBounds.Height * 0.58f);
        canvas.DrawLine(
            new Vector2(contentBounds.Left, horizonY),
            new Vector2(contentBounds.Right, horizonY),
            scene.Theme.AmbientGridColor.WithAlpha(26), 1.2f);

        for (var ribbonIndex = 0; ribbonIndex < ribbonCount; ribbonIndex++)
        {
            var points = BuildRibbonPoints(contentBounds, scene, sampleCount, ribbonIndex, phase);
            var curve = VizMath.CardinalSpline(points, 0.42f, 4);

            var width = 28f - (ribbonIndex * 3.5f);
            canvas.DrawPolyline(curve, ribbonColors[ribbonIndex].WithAlpha(34), width, roundCap: true);
            canvas.DrawPolyline(curve, ribbonColors[ribbonIndex].WithAlpha(185), Math.Max(3.2f, width * 0.24f), roundCap: true);

            DrawRibbonHighlights(canvas, points, ribbonColors[ribbonIndex], ribbonIndex);
        }
    }

    private static Vector2[] BuildRibbonPoints(
        VizRect contentBounds,
        VisualizerScene scene,
        int sampleCount,
        int ribbonIndex,
        float phase)
    {
        var points = new Vector2[sampleCount];
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

            points[sample] = new Vector2(x, y);
        }

        return points;
    }

    private static void DrawRibbonHighlights(IVizCanvas canvas, Vector2[] points, VizColor color, int ribbonIndex)
    {
        var step = Math.Max(8, 18 - (ribbonIndex * 2));
        var highlight = color.WithAlpha(86 - (ribbonIndex * 10));

        for (var index = ribbonIndex + 2; index < points.Length; index += step)
        {
            var size = Math.Max(4f, 10f - ribbonIndex);
            canvas.FillEllipse(
                new VizRect(points[index].X - (size / 2f), points[index].Y - (size / 2f), size, size),
                highlight);
        }
    }
}
