using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

/// <summary>
/// Scrolling peak-envelope waveform — same time-axis idea as SpectrogramRenderer (a ring
/// buffer of per-frame columns, newest on the right, oldest scrolling off the left) rather
/// than redrawing the raw WaveformPoints buffer fresh every frame. That raw-buffer approach
/// looked chaotic: WaveformPoints is just whatever ~16ms slice the engine last captured, so two
/// consecutive 60fps frames could show unrelated slices with no visual continuity between them.
/// Collapsing each frame's slice to one peak value and scrolling it in gives a continuously
/// flowing strip instead.
/// </summary>
public sealed class WaveformRenderer : VisualizerRendererBase
{
    // ~2s of history at 60fps. Short on purpose — "fast" scroll was the ask, not a long tape.
    private const int HistoryCapacity = 120;

    private readonly float[] _history = new float[HistoryCapacity];
    private int _newestIndex = -1;
    private int _filledCount;

    private Vector2[]? _topPoints;
    private Vector2[]? _envelope;

    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);
        DrawGrid(canvas, bounds, scene);
        PushColumn(scene);
        DrawWaveform(canvas, bounds, scene);
        DrawHud(canvas, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(canvas, bounds, scene);
    }

    private void PushColumn(VisualizerScene scene)
    {
        var peak = 0f;
        foreach (var sample in scene.WaveformPoints)
        {
            var abs = Math.Abs(sample);
            if (abs > peak)
                peak = abs;
        }

        _newestIndex = (_newestIndex + 1) % HistoryCapacity;
        _history[_newestIndex] = peak;
        _filledCount = Math.Min(_filledCount + 1, HistoryCapacity);
    }

    // Filled, mirrored amplitude envelope — the classic DAW/editor "waveform" silhouette.
    // Deliberately not a single wandering trace (that's the Oscilloscope's whole identity):
    // taking the magnitude and mirroring it above/below center turns the same history buffer
    // into a shape that reads as "audio waveform" at a glance instead of a scan line.
    private void DrawWaveform(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var contentBounds = bounds.Inflate(-18, -24);
        var centerY = contentBounds.Top + (contentBounds.Height / 2f);

        canvas.DrawLine(
            new Vector2(contentBounds.Left, centerY),
            new Vector2(contentBounds.Right, centerY),
            scene.Theme.HudLabelColor.WithAlpha(72), 1.5f);

        var count = _filledCount;
        if (count < 2)
            return;

        if (_topPoints is null || _topPoints.Length != count)
            _topPoints = new Vector2[count];

        for (var index = 0; index < count; index++)
        {
            // index 0 = oldest/leftmost, index count-1 = newest/rightmost — same "age from
            // newest" ring-buffer convention SpectrogramRenderer uses for its time axis.
            var age = count - 1 - index;
            var histIndex = (((_newestIndex - age) % HistoryCapacity) + HistoryCapacity) % HistoryCapacity;
            var x = contentBounds.Left + (index / (float)(count - 1) * contentBounds.Width);
            var amplitude = _history[histIndex] * (contentBounds.Height * 0.45f);
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
