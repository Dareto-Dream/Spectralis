using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

internal sealed class SpectrumBarsVisualizerRenderer(bool mirrored) : VisualizerRendererBase
{
    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        DrawBackground(graphics, bounds, scene);
        DrawGrid(graphics, bounds, scene);
        DrawSpectrumBars(graphics, bounds, scene);
        DrawHud(graphics, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(graphics, bounds, scene);
    }

    private void DrawSpectrumBars(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var contentBounds = Rectangle.Inflate(bounds, -18, -18);
        var displayBars = Math.Clamp(contentBounds.Width / 14, 18, scene.SpectrumLevels.Length);
        var gap = Math.Max(3, contentBounds.Width / (displayBars * 7));
        var totalGapWidth = gap * (displayBars - 1);
        var barWidth = Math.Max(5, (contentBounds.Width - totalGapWidth) / displayBars);
        var cornerRadius = Math.Max(4, barWidth / 2);
        var centerY = contentBounds.Top + (contentBounds.Height / 2);

        using var glowBrush = new SolidBrush(Color.FromArgb(22, scene.Theme.BarGlowColor));
        using var fillBrush = new LinearGradientBrush(
            contentBounds,
            scene.Theme.BarStartColor,
            scene.Theme.BarEndColor,
            LinearGradientMode.Vertical);
        using var peakPen = new Pen(Color.FromArgb(210, scene.Theme.PeakColor), 2);

        for (var index = 0; index < displayBars; index++)
        {
            var level = SampleRange(scene.SpectrumLevels, index, displayBars);
            var barHeight = Math.Max(6, (int)((mirrored ? contentBounds.Height / 2f : contentBounds.Height) * level));
            var x = contentBounds.Left + (index * (barWidth + gap));

            if (mirrored)
            {
                var upperRect = new Rectangle(x, centerY - barHeight, barWidth, Math.Max(2, barHeight - 2));
                var lowerRect = new Rectangle(x, centerY + 2, barWidth, Math.Max(2, barHeight - 2));

                graphics.FillRectangle(glowBrush, Rectangle.Inflate(upperRect, 0, 4));
                graphics.FillRectangle(glowBrush, Rectangle.Inflate(lowerRect, 0, 4));

                using var upperPath = CreateRoundedRectangle(upperRect, cornerRadius);
                using var lowerPath = CreateRoundedRectangle(lowerRect, cornerRadius);
                graphics.FillPath(fillBrush, upperPath);
                graphics.FillPath(fillBrush, lowerPath);

                if (scene.ShowPeaks)
                {
                    var peakHeight = (int)((contentBounds.Height / 2f) * SampleRange(scene.PeakHoldLevels, index, displayBars));
                    var peakY = centerY - peakHeight - 4;
                    graphics.DrawLine(peakPen, x + 1, peakY, x + barWidth - 1, peakY);
                    graphics.DrawLine(peakPen, x + 1, centerY + peakHeight + 4, x + barWidth - 1, centerY + peakHeight + 4);
                }
            }
            else
            {
                var y = contentBounds.Bottom - barHeight;
                var barRect = new Rectangle(x, y, barWidth, barHeight);

                graphics.FillRectangle(glowBrush, Rectangle.Inflate(barRect, 0, 4));

                using var path = CreateRoundedRectangle(barRect, cornerRadius);
                graphics.FillPath(fillBrush, path);

                if (scene.ShowPeaks)
                {
                    var peakHeight = (int)(contentBounds.Height * SampleRange(scene.PeakHoldLevels, index, displayBars));
                    var peakY = contentBounds.Bottom - peakHeight;
                    graphics.DrawLine(peakPen, x + 1, peakY, x + barWidth - 1, peakY);
                }
            }
        }
    }
}
