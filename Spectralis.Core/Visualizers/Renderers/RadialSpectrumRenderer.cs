using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

public sealed class RadialSpectrumRenderer : VisualizerRendererBase
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);
        DrawRadialSpectrum(canvas, bounds, scene);
        DrawHud(canvas, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(canvas, bounds, scene);
    }

    private static void DrawRadialSpectrum(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var cx = bounds.Left + (bounds.Width / 2f);
        var cy = bounds.Top + (bounds.Height / 2f);
        var maxRadius = (Math.Min(bounds.Width, bounds.Height) / 2f) - 22;
        var innerRadius = maxRadius * 0.30f;

        var displayBars = Math.Clamp(scene.SpectrumLevels.Length, 32, 64);
        var angleStep = 360f / displayBars;

        var glowColor = scene.Theme.BarGlowColor.WithAlpha(30);
        var barColor = scene.Theme.BarStartColor.WithAlpha(210);
        var peakColor = scene.Theme.PeakColor.WithAlpha(180);
        var rotationOffset = scene.AnimationPhase;

        for (var index = 0; index < displayBars; index++)
        {
            var level = SampleRange(scene.SpectrumLevels, index, displayBars);
            var barLength = Math.Max(4f, (maxRadius - innerRadius) * level);
            var angleRad = ((index * angleStep) + rotationOffset) * MathF.PI / 180f;

            var cosA = MathF.Cos(angleRad);
            var sinA = MathF.Sin(angleRad);

            var start = new Vector2(cx + (cosA * innerRadius), cy + (sinA * innerRadius));
            var end = new Vector2(cx + (cosA * (innerRadius + barLength)), cy + (sinA * (innerRadius + barLength)));

            canvas.DrawLine(start, end, glowColor, 7f, roundCap: true);
            canvas.DrawLine(start, end, barColor, 2.5f, roundCap: true);

            if (scene.ShowPeaks)
            {
                var peak = SampleRange(scene.PeakHoldLevels, index, displayBars);
                var peakR = innerRadius + Math.Max(4f, (maxRadius - innerRadius) * peak);
                var px = cx + (cosA * peakR);
                var py = cy + (sinA * peakR);
                canvas.DrawLine(
                    new Vector2(px - (cosA * 2f), py - (sinA * 2f)),
                    new Vector2(px + (cosA * 2f), py + (sinA * 2f)),
                    peakColor, 1.5f, roundCap: true);
            }
        }

        // Inner ring
        canvas.DrawEllipse(
            new VizRect(cx - innerRadius, cy - innerRadius, innerRadius * 2, innerRadius * 2),
            scene.Theme.RingColor.WithAlpha(60), 1.5f);

        // Hub
        var hubR = innerRadius * 0.52f;
        canvas.FillEllipse(new VizRect(cx - hubR, cy - hubR, hubR * 2, hubR * 2), scene.Theme.HubColor.WithAlpha(185));

        var dotR = hubR * 0.40f;
        canvas.FillEllipse(new VizRect(cx - dotR, cy - dotR, dotR * 2, dotR * 2), scene.Theme.HubDotColor);
    }
}
