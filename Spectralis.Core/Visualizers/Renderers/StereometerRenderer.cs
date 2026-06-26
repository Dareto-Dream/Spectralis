using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

/// <summary>
/// Lissajous / stereometer: plots L vs R rotated 45° so mono (L==R) is a vertical
/// line. Dots fade from transparent (oldest sample) to solid (newest). A
/// correlation bar at the bottom shows dot(L,R)/sqrt(E_L·E_R) on a −1…+1 scale.
/// </summary>
public sealed class StereometerRenderer : VisualizerRendererBase
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);

        var plotHeight = bounds.Height - 44;
        var plotRect = new VizRect(bounds.Left, bounds.Top, bounds.Width, Math.Max(plotHeight, 60));

        DrawLissajous(canvas, plotRect, scene);
        DrawCorrelationBar(canvas, bounds, scene);
        DrawHud(canvas, bounds, scene);
    }

    private static void DrawLissajous(IVizCanvas canvas, VizRect plotRect, VisualizerScene scene)
    {
        var left = scene.WaveformLeft;
        var right = scene.WaveformRight;
        if (left.Length == 0 || right.Length == 0)
        {
            return;
        }

        var count = Math.Min(left.Length, right.Length);
        var cx = plotRect.Left + (plotRect.Width * 0.5f);
        var cy = plotRect.Top + (plotRect.Height * 0.5f);
        var half = Math.Min(plotRect.Width, plotRect.Height) * 0.44f;
        var inv2 = 1f / MathF.Sqrt(2f);

        var guideColor = scene.Theme.AmbientGridColor.WithAlpha(28);
        canvas.DrawLine(new Vector2(cx, plotRect.Top + 6), new Vector2(cx, plotRect.Bottom - 6), guideColor, 1f);
        canvas.DrawLine(new Vector2(plotRect.Left + 6, cy), new Vector2(plotRect.Right - 6, cy), guideColor, 1f);

        for (var i = 0; i < count; i++)
        {
            // 45° rotation: mid = (L+R)/√2, side = (L−R)/√2; Y is flipped for screen space.
            var side = (left[i] - right[i]) * inv2;
            var mid = (left[i] + right[i]) * inv2;
            var sx = cx + (side * half);
            var sy = cy - (mid * half);

            var alpha = (int)(20 + (200f * i / count));
            var dotSize = i > count * 0.85f ? 2.5f : 1.5f;
            canvas.FillEllipse(
                new VizRect(sx - (dotSize * 0.5f), sy - (dotSize * 0.5f), dotSize, dotSize),
                scene.Theme.AmbientGlowColor.WithAlpha(alpha));
        }

        canvas.DrawEllipse(
            new VizRect(cx - half, cy - half, half * 2, half * 2),
            scene.Theme.AmbientGridColor.WithAlpha(22),
            1f);
    }

    private static void DrawCorrelationBar(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        const float barHeight = 22;
        const float margin = 20;

        var barRect = new VizRect(
            bounds.Left + margin,
            bounds.Bottom - barHeight - 4,
            bounds.Width - (margin * 2),
            barHeight);

        canvas.FillRoundedRect(barRect, 4, new VizColor(48, 255, 255, 255));

        var corr = Math.Clamp(scene.StereoCorrelation, -1f, 1f);
        var cx = barRect.Left + (barRect.Width / 2);

        if (Math.Abs(corr) > 0.005f)
        {
            var fillWidth = Math.Abs(corr) * barRect.Width / 2;
            var fillX = corr > 0 ? cx : cx - fillWidth;
            if (fillWidth > 0)
            {
                var fillColor = corr > 0
                    ? new VizColor(180, 60, 200, 80)
                    : new VizColor(180, 200, 60, 60);
                canvas.FillRoundedRect(new VizRect(fillX, barRect.Top, fillWidth, barRect.Height), 3, fillColor);
            }
        }

        canvas.DrawLine(
            new Vector2(cx, barRect.Top + 2),
            new Vector2(cx, barRect.Bottom - 2),
            new VizColor(80, 255, 255, 255),
            1f);

        var labelColor = scene.Theme.HudInfoColor.WithAlpha(160);
        canvas.DrawText("-1", new VizRect(barRect.Left + 4, barRect.Top + 4, 24, 14), labelColor, 10, VizTextAlign.Left);
        canvas.DrawText("+1", new VizRect(barRect.Right - 28, barRect.Top + 4, 24, 14), labelColor, 10, VizTextAlign.Right);
        canvas.DrawText(
            $"ρ {corr:+0.00;-0.00;0.00}",
            new VizRect(barRect.Left, barRect.Top + 4, barRect.Width, 14),
            labelColor,
            10,
            VizTextAlign.Center);
    }
}
