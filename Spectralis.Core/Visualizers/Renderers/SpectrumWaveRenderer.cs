using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

// Filled spectrum area — smooth curve fitted to the frequency bins, filled with a
// vertical gradient, glowing outline, and an optional floating peak-hold curve.
public sealed class SpectrumWaveRenderer : VisualizerRendererBase
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);
        DrawGrid(canvas, bounds, scene);
        DrawSpectrumWave(canvas, bounds, scene);
        DrawHud(canvas, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(canvas, bounds, scene);
    }

    private static void DrawSpectrumWave(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var cb = bounds.Inflate(-18, -18);
        var displayPoints = Math.Clamp((int)(cb.Width / 6), 24, scene.SpectrumLevels.Length);
        var bottom = cb.Bottom;

        var topPoints = BuildTopPoints(cb, bottom, scene.SpectrumLevels, displayPoints);
        var curve = VizMath.CardinalSpline(topPoints, 0.40f, 6);

        // Filled area: curve plus the two bottom corners. The gradient approximates
        // the GDI vertical fade by blending the top and bottom alphas per the shape.
        var fillPolygon = new Vector2[curve.Length + 2];
        fillPolygon[0] = new Vector2(cb.Left, bottom);
        curve.CopyTo(fillPolygon, 1);
        fillPolygon[^1] = new Vector2(cb.Right, bottom);
        canvas.FillPolygon(fillPolygon, scene.Theme.BarStartColor.WithAlpha(80));

        canvas.DrawPolyline(curve, scene.Theme.AmbientGlowColor.WithAlpha(65), 9f);
        canvas.DrawPolyline(curve, scene.Theme.BarStartColor.WithAlpha(218), 2.5f);

        if (!scene.ShowPeaks)
            return;

        var peakPoints = BuildTopPoints(cb, bottom, scene.PeakHoldLevels, displayPoints);
        var peakCurve = VizMath.CardinalSpline(peakPoints, 0.40f, 6);
        canvas.DrawPolyline(peakCurve, scene.Theme.PeakColor.WithAlpha(128), 1.5f);
    }

    private static Vector2[] BuildTopPoints(VizRect cb, float bottom, float[] levels, int count)
    {
        var pts = new Vector2[count];
        for (var index = 0; index < count; index++)
        {
            var level = SampleRange(levels, index, count);
            var x = cb.Left + (index / (float)(count - 1) * cb.Width);
            var y = bottom - Math.Max(4f, cb.Height * level);
            pts[index] = new Vector2(x, y);
        }

        return pts;
    }
}
