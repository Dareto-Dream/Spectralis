using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

// Phosphor-glow oscilloscope — bright scan line with CRT-style outer glow and dot markers.
public sealed class OscilloscopeRenderer : VisualizerRendererBase
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);
        DrawOscilloscope(canvas, bounds, scene);
        DrawHud(canvas, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(canvas, bounds, scene);
    }

    private static void DrawOscilloscope(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var contentBounds = bounds.Inflate(-18, -28);
        var centerY = contentBounds.Top + (contentBounds.Height / 2f);
        var halfHeight = contentBounds.Height * 0.44f;

        canvas.DrawLine(
            new Vector2(contentBounds.Left, centerY),
            new Vector2(contentBounds.Right, centerY),
            scene.Theme.HudLabelColor.WithAlpha(28), 1f);

        if (scene.WaveformPoints.Length < 2)
            return;

        var controlPoints = new Vector2[scene.WaveformPoints.Length];
        for (var index = 0; index < scene.WaveformPoints.Length; index++)
        {
            var x = contentBounds.Left + (index / (float)(scene.WaveformPoints.Length - 1)) * contentBounds.Width;
            controlPoints[index] = new Vector2(x, centerY - (scene.WaveformPoints[index] * halfHeight));
        }

        var curve = VizMath.CardinalSpline(controlPoints, 0.28f, 4);
        canvas.DrawPolyline(curve, scene.Theme.AmbientGlowColor.WithAlpha(18), 18f, roundCap: true);
        canvas.DrawPolyline(curve, scene.Theme.BarGlowColor.WithAlpha(58), 6f, roundCap: true);
        canvas.DrawPolyline(curve, scene.Theme.BarStartColor.WithAlpha(228), 2f, roundCap: true);

        // Phosphor dot markers along the trace
        var dotColor = scene.Theme.BarEndColor.WithAlpha(175);
        var dotStep = Math.Max(3, scene.WaveformPoints.Length / 90);
        for (var index = 0; index < scene.WaveformPoints.Length; index += dotStep)
        {
            var x = contentBounds.Left + (index / (float)(scene.WaveformPoints.Length - 1)) * contentBounds.Width;
            var y = centerY - (scene.WaveformPoints[index] * halfHeight);
            canvas.FillEllipse(new VizRect(x - 2f, y - 2f, 4f, 4f), dotColor);
        }
    }
}
