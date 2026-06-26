using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

public sealed class WaveformRenderer : VisualizerRendererBase
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);
        DrawGrid(canvas, bounds, scene);
        DrawWaveform(canvas, bounds, scene);
        DrawHud(canvas, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(canvas, bounds, scene);
    }

    private static void DrawWaveform(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var contentBounds = bounds.Inflate(-18, -24);
        var centerY = contentBounds.Top + (contentBounds.Height / 2f);

        canvas.DrawLine(
            new Vector2(contentBounds.Left, centerY),
            new Vector2(contentBounds.Right, centerY),
            scene.Theme.HudLabelColor.WithAlpha(72), 1.5f);

        var controlPoints = new Vector2[scene.WaveformPoints.Length];
        for (var index = 0; index < scene.WaveformPoints.Length; index++)
        {
            var x = contentBounds.Left + (index / (float)(scene.WaveformPoints.Length - 1) * contentBounds.Width);
            var y = centerY - (scene.WaveformPoints[index] * (contentBounds.Height * 0.45f));
            controlPoints[index] = new Vector2(x, y);
        }

        var curve = VizMath.CardinalSpline(controlPoints, 0.35f, 4);
        canvas.DrawPolyline(curve, scene.Theme.AmbientGlowColor.WithAlpha(40), 8, roundCap: true);
        canvas.DrawPolyline(curve, scene.Theme.BarStartColor.WithAlpha(240), 3, roundCap: true);
    }
}
