using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

public sealed class SpectrumBarsRenderer(bool mirrored) : VisualizerRendererBase
{
    // The FFT that feeds SpectrumLevels only completes every ~185ms, but this renders
    // at 60fps — without easing, every bar holds still for ~11 frames then hard-snaps
    // to the next value, which reads as choppy/stuttery. Ease the displayed height
    // toward the latest target each frame instead of drawing the target directly.
    private const float SmoothingPerFrame = 0.3f;
    private float[]? _displayLevels;

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

        if (_displayLevels is null || _displayLevels.Length != displayBars)
        {
            // (Re)seed at the current target on first draw or bar-count change (window
            // resize) so it doesn't visibly animate up from zero.
            _displayLevels = new float[displayBars];
            for (var seedIndex = 0; seedIndex < displayBars; seedIndex++)
                _displayLevels[seedIndex] = SampleRange(scene.SpectrumLevels, seedIndex, displayBars);
        }

        for (var index = 0; index < displayBars; index++)
        {
            var target = SampleRange(scene.SpectrumLevels, index, displayBars);
            _displayLevels[index] += (target - _displayLevels[index]) * SmoothingPerFrame;
            var level = _displayLevels[index];
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
