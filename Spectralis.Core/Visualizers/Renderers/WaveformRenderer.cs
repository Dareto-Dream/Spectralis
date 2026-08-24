using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

public sealed class WaveformRenderer : VisualizerRendererBase
{
    private Vector2[]? _topPoints;
    private Vector2[]? _envelope;

    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);
        DrawGrid(canvas, bounds, scene);
        DrawWaveform(canvas, bounds, scene);
        DrawHud(canvas, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(canvas, bounds, scene);
    }

    // Filled, mirrored amplitude envelope — the classic DAW/editor "waveform" silhouette.
    // Deliberately not a single wandering trace (that's the Oscilloscope's whole identity):
    // taking the magnitude and mirroring it above/below center turns the same WaveformPoints
    // buffer into a shape that reads as "audio waveform" at a glance instead of a scan line.
    private void DrawWaveform(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var contentBounds = bounds.Inflate(-18, -24);
        var centerY = contentBounds.Top + (contentBounds.Height / 2f);

        canvas.DrawLine(
            new Vector2(contentBounds.Left, centerY),
            new Vector2(contentBounds.Right, centerY),
            scene.Theme.HudLabelColor.WithAlpha(72), 1.5f);

        var count = scene.WaveformPoints.Length;
        if (count < 2)
            return;

        if (_topPoints is null || _topPoints.Length != count)
            _topPoints = new Vector2[count];

        for (var index = 0; index < count; index++)
        {
            var x = contentBounds.Left + (index / (float)(count - 1) * contentBounds.Width);
            var amplitude = Math.Abs(scene.WaveformPoints[index]) * (contentBounds.Height * 0.45f);
            _topPoints[index] = new Vector2(x, centerY - amplitude);
        }

        // Only the top control points ever get spline-expanded. The bottom curve is the exact
        // mirror image about centerY — not an approximation: the cardinal-spline Hermite basis
        // weights on the two position terms (h00, h01) always sum to 1, so reflecting every
        // control point about a line and reflecting the spline's own output about that same line
        // give identical results. Running CardinalSpline (which heap-allocates a List internally)
        // a second time for a curve fully derivable from the first was measurably too slow to
        // hold 60fps — see VisualizerSustainedBenchmark.
        var topCurve = VizMath.CardinalSpline(_topPoints, 0.35f, 4);

        if (_envelope is null || _envelope.Length != topCurve.Length * 2)
            _envelope = new Vector2[topCurve.Length * 2];

        var bottomStart = topCurve.Length;
        for (var index = 0; index < topCurve.Length; index++)
        {
            var top = topCurve[index];
            _envelope[index] = top;
            _envelope[bottomStart + (topCurve.Length - 1 - index)] = new Vector2(top.X, (2 * centerY) - top.Y);
        }

        canvas.FillPolygon(_envelope, scene.Theme.AmbientGlowColor.WithAlpha(30));
        canvas.FillPolygon(_envelope, scene.Theme.BarStartColor.WithAlpha(110));
        canvas.DrawPolyline(_envelope.AsSpan(0, topCurve.Length), scene.Theme.BarEndColor.WithAlpha(235), 2.5f, roundCap: true);
        canvas.DrawPolyline(_envelope.AsSpan(bottomStart, topCurve.Length), scene.Theme.BarEndColor.WithAlpha(235), 2.5f, roundCap: true);
    }
}
