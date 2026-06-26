using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

// Lissajous / stereometer: plots L vs R rotated 45° so mono (L==R) is a vertical line.
// Dots fade from transparent (oldest sample) to solid (newest).
// A correlation bar at the bottom shows dot(L,R)/sqrt(E_L·E_R) on a −1…+1 scale.
internal sealed class StereometerVisualizerRenderer : VisualizerRendererBase
{
    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        DrawBackground(graphics, bounds, scene);

        var plotH = bounds.Height - 44;  // leave room for correlation bar + HUD
        var plotRect = new Rectangle(bounds.Left, bounds.Top, bounds.Width, Math.Max(plotH, 60));

        DrawLissajous(graphics, plotRect, scene);
        DrawCorrelationBar(graphics, bounds, scene);
        DrawHud(graphics, bounds, scene);
    }

    private static void DrawLissajous(Graphics graphics, Rectangle plotRect, VisualizerScene scene)
    {
        var wL = scene.WaveformL;
        var wR = scene.WaveformR;
        if (wL.Length == 0 || wR.Length == 0) return;

        var n = Math.Min(wL.Length, wR.Length);
        var cx = plotRect.Left + plotRect.Width  * 0.5f;
        var cy = plotRect.Top  + plotRect.Height * 0.5f;
        var half = Math.Min(plotRect.Width, plotRect.Height) * 0.44f;
        var inv2 = 1f / MathF.Sqrt(2f);

        // Guide lines: mid axis (vertical) and side axis (horizontal), very faint
        using var guidePen = new Pen(Color.FromArgb(28, scene.Theme.AmbientGridColor), 1f);
        graphics.DrawLine(guidePen, cx, plotRect.Top + 6, cx, plotRect.Bottom - 6);
        graphics.DrawLine(guidePen, plotRect.Left + 6, cy, plotRect.Right - 6, cy);

        // Draw the scatter trail: older samples are transparent, newer are solid
        for (var i = 0; i < n; i++)
        {
            var l = wL[i];
            var r = wR[i];

            // 45° rotation: mid = (L+R)/√2, side = (L-R)/√2
            var side = (l - r) * inv2;
            var mid  = (l + r) * inv2;

            var sx = cx + side * half;
            var sy = cy - mid  * half;   // Y flipped (screen Y is down)

            var alpha = (int)(20 + 200f * i / n);
            var dotColor = Color.FromArgb(alpha, scene.Theme.AmbientGlowColor);

            var dotSize = i > n * 0.85f ? 2.5f : 1.5f;
            using var dot = new SolidBrush(dotColor);
            graphics.FillEllipse(dot, sx - dotSize * 0.5f, sy - dotSize * 0.5f, dotSize, dotSize);
        }

        // Outer circle guide
        using var circlePen = new Pen(Color.FromArgb(22, scene.Theme.AmbientGridColor), 1f);
        graphics.DrawEllipse(circlePen, cx - half, cy - half, half * 2, half * 2);
    }

    private static void DrawCorrelationBar(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        const int barH = 22;
        const int margin = 20;

        var barRect = new Rectangle(bounds.Left + margin, bounds.Bottom - barH - 4, bounds.Width - margin * 2, barH);

        // Background track
        using var trackBrush = new SolidBrush(Color.FromArgb(48, 255, 255, 255));
        using var trackPath = CreateRoundedRectangle(barRect, 4);
        graphics.FillPath(trackBrush, trackPath);

        var corr = Math.Clamp(scene.StereoCorrelation, -1f, 1f);
        var cx   = barRect.Left + barRect.Width / 2;

        if (Math.Abs(corr) > 0.005f)
        {
            var fillW = (int)(Math.Abs(corr) * barRect.Width / 2);
            var fillX = corr > 0 ? cx : cx - fillW;
            var fillRect = new Rectangle(fillX, barRect.Top, fillW, barRect.Height);
            if (fillRect.Width > 0)
            {
                var fillColor = corr > 0
                    ? Color.FromArgb(180, 60, 200, 80)
                    : Color.FromArgb(180, 200, 60, 60);
                using var fillBrush = new SolidBrush(fillColor);
                using var fillPath = CreateRoundedRectangle(fillRect, 3);
                graphics.FillPath(fillBrush, fillPath);
            }
        }

        // Centre divider
        using var divPen = new Pen(Color.FromArgb(80, 255, 255, 255), 1f);
        graphics.DrawLine(divPen, cx, barRect.Top + 2, cx, barRect.Bottom - 2);

        // Labels
        using var labelFont  = new Font("Segoe UI", 7f, FontStyle.Regular, GraphicsUnit.Point);
        using var labelBrush = new SolidBrush(Color.FromArgb(160, scene.Theme.HudInfoColor));
        using var fmt        = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString("-1", labelFont, labelBrush, barRect.Left  + 8, barRect.Top + barH / 2f);
        graphics.DrawString("+1", labelFont, labelBrush, barRect.Right - 8, barRect.Top + barH / 2f);
        graphics.DrawString($"ρ {corr:+0.00;-0.00;0.00}", labelFont, labelBrush,
                            new RectangleF(barRect.Left, barRect.Top, barRect.Width, barRect.Height), fmt);
    }
}
