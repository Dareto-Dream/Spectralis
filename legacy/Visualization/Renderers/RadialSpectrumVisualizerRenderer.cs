using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

internal sealed class RadialSpectrumVisualizerRenderer : VisualizerRendererBase
{
    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        DrawBackground(graphics, bounds, scene);
        DrawRadialSpectrum(graphics, bounds, scene);
        DrawHud(graphics, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(graphics, bounds, scene);
    }

    private static void DrawRadialSpectrum(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var cx = bounds.Left + bounds.Width / 2f;
        var cy = bounds.Top + bounds.Height / 2f;
        var maxRadius = (Math.Min(bounds.Width, bounds.Height) / 2f) - 22;
        var innerRadius = maxRadius * 0.30f;

        var displayBars = Math.Clamp(scene.SpectrumLevels.Length, 32, 64);
        var angleStep = 360f / displayBars;

        using var glowPen = new Pen(Color.FromArgb(30, scene.Theme.BarGlowColor), 7f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var barPen = new Pen(Color.FromArgb(210, scene.Theme.BarStartColor), 2.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        using var peakPen = new Pen(Color.FromArgb(180, scene.Theme.PeakColor), 1.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        var rotationOffset = scene.AnimationPhase;

        for (var index = 0; index < displayBars; index++)
        {
            var level = SampleRange(scene.SpectrumLevels, index, displayBars);
            var barLength = Math.Max(4f, (maxRadius - innerRadius) * level);
            var angleRad = ((index * angleStep) + rotationOffset) * MathF.PI / 180f;

            var cosA = MathF.Cos(angleRad);
            var sinA = MathF.Sin(angleRad);

            var x1 = cx + cosA * innerRadius;
            var y1 = cy + sinA * innerRadius;
            var x2 = cx + cosA * (innerRadius + barLength);
            var y2 = cy + sinA * (innerRadius + barLength);

            graphics.DrawLine(glowPen, x1, y1, x2, y2);
            graphics.DrawLine(barPen, x1, y1, x2, y2);

            if (scene.ShowPeaks)
            {
                var peak = SampleRange(scene.PeakHoldLevels, index, displayBars);
                var peakR = innerRadius + Math.Max(4f, (maxRadius - innerRadius) * peak);
                var px = cx + cosA * peakR;
                var py = cy + sinA * peakR;
                graphics.DrawLine(peakPen, px - cosA * 2f, py - sinA * 2f, px + cosA * 2f, py + sinA * 2f);
            }
        }

        // Inner ring
        using var ringPen = new Pen(Color.FromArgb(60, scene.Theme.RingColor), 1.5f);
        graphics.DrawEllipse(ringPen, cx - innerRadius, cy - innerRadius, innerRadius * 2, innerRadius * 2);

        // Hub
        var hubR = innerRadius * 0.52f;
        using var hubBrush = new SolidBrush(Color.FromArgb(185, scene.Theme.HubColor));
        graphics.FillEllipse(hubBrush, cx - hubR, cy - hubR, hubR * 2, hubR * 2);

        var dotR = hubR * 0.40f;
        using var dotBrush = new SolidBrush(scene.Theme.HubDotColor);
        graphics.FillEllipse(dotBrush, cx - dotR, cy - dotR, dotR * 2, dotR * 2);
    }
}
