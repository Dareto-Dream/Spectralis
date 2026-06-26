using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

public sealed class SpectrumBarsRenderer(bool mirrored) : VisualizerRendererBase
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);
        DrawGrid(canvas, bounds, scene);
        DrawSpectrumBars(canvas, bounds, scene);
        DrawHud(canvas, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(canvas, bounds, scene);
    }

    private void DrawSpectrumBars(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var contentBounds = bounds.Inflate(-18, -18);
        var displayBars = Math.Clamp((int)(contentBounds.Width / 14), 18, scene.SpectrumLevels.Length);
        var gap = Math.Max(3, (int)(contentBounds.Width / (displayBars * 7)));
        var totalGapWidth = gap * (displayBars - 1);
        var barWidth = Math.Max(5, (int)((contentBounds.Width - totalGapWidth) / displayBars));
        var cornerRadius = Math.Max(4, barWidth / 2);
        var centerY = contentBounds.Top + (contentBounds.Height / 2);

        var glowColor = scene.Theme.BarGlowColor.WithAlpha(22);
        var peakColor = scene.Theme.PeakColor.WithAlpha(210);

        for (var index = 0; index < displayBars; index++)
        {
            var level = SampleRange(scene.SpectrumLevels, index, displayBars);
            var barHeight = Math.Max(6, (int)((mirrored ? contentBounds.Height / 2f : contentBounds.Height) * level));
            var x = contentBounds.Left + (index * (barWidth + gap));

            if (mirrored)
            {
                var upperRect = new VizRect(x, centerY - barHeight, barWidth, Math.Max(2, barHeight - 2));
                var lowerRect = new VizRect(x, centerY + 2, barWidth, Math.Max(2, barHeight - 2));

                canvas.FillRect(upperRect.Inflate(0, 4), glowColor);
                canvas.FillRect(lowerRect.Inflate(0, 4), glowColor);
                canvas.FillRoundedRectGradientV(upperRect, cornerRadius, scene.Theme.BarStartColor, scene.Theme.BarEndColor);
                canvas.FillRoundedRectGradientV(lowerRect, cornerRadius, scene.Theme.BarStartColor, scene.Theme.BarEndColor);

                if (scene.ShowPeaks)
                {
                    var peakHeight = (int)((contentBounds.Height / 2f) * SampleRange(scene.PeakHoldLevels, index, displayBars));
                    var peakY = centerY - peakHeight - 4;
                    canvas.DrawLine(new Vector2(x + 1, peakY), new Vector2(x + barWidth - 1, peakY), peakColor, 2);
                    canvas.DrawLine(
                        new Vector2(x + 1, centerY + peakHeight + 4),
                        new Vector2(x + barWidth - 1, centerY + peakHeight + 4),
                        peakColor, 2);
                }
            }
            else
            {
                var y = contentBounds.Bottom - barHeight;
                var barRect = new VizRect(x, y, barWidth, barHeight);

                canvas.FillRect(barRect.Inflate(0, 4), glowColor);
                canvas.FillRoundedRectGradientV(barRect, cornerRadius, scene.Theme.BarStartColor, scene.Theme.BarEndColor);

                if (scene.ShowPeaks)
                {
                    var peakHeight = (int)(contentBounds.Height * SampleRange(scene.PeakHoldLevels, index, displayBars));
                    var peakY = contentBounds.Bottom - peakHeight;
                    canvas.DrawLine(new Vector2(x + 1, peakY), new Vector2(x + barWidth - 1, peakY), peakColor, 2);
                }
            }
        }
    }
}
