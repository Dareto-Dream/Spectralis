using System.Numerics;

namespace Spectralis.Core.Visualizers;

public interface IVisualizerRenderer
{
    void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene);
}

public abstract class VisualizerRendererBase : IVisualizerRenderer
{
    public abstract void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene);

    protected static void DrawBackground(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var glowStrength = Math.Clamp((scene.PeakLevel * 0.45f) + (scene.RmsLevel * 0.95f), 0.05f, 0.75f);

        canvas.FillRectGradientV(bounds, scene.Theme.BackgroundTopColor, scene.Theme.BackgroundBottomColor);
        canvas.FillRadialGlow(
            new VizRect(bounds.Left, bounds.Top + (bounds.Height * 0.25f), bounds.Width, bounds.Height * 0.75f),
            scene.Theme.AmbientGlowColor.WithAlpha((int)(92 * glowStrength)));
    }

    protected static void DrawGrid(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var horizontal = scene.Theme.AmbientGridColor.WithAlpha(22);
        var vertical = scene.Theme.AmbientGridColor.WithAlpha(14);

        for (var index = 1; index < 5; index++)
        {
            var y = bounds.Top + (index * bounds.Height / 5);
            canvas.DrawLine(new Vector2(bounds.Left, y), new Vector2(bounds.Right, y), horizontal, 1);
        }

        for (var index = 1; index < 10; index++)
        {
            var x = bounds.Left + (index * bounds.Width / 10);
            canvas.DrawLine(new Vector2(x, bounds.Top), new Vector2(x, bounds.Bottom), vertical, 1);
        }
    }

    protected static void DrawHud(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var hudBounds = new VizRect(bounds.Left + 18, bounds.Top + 14, bounds.Width - 36, 40);
        const float meterWidth = 120;
        const float meterHeight = 8;
        var meterRect = new VizRect(hudBounds.Right - meterWidth, hudBounds.Top + 12, meterWidth, meterHeight);

        canvas.DrawText(scene.ModeLabel, new VizRect(hudBounds.Left, hudBounds.Top, 240, 16), scene.Theme.HudLabelColor, 12, VizTextAlign.Left);
        canvas.DrawText(scene.IsActive ? "Live" : "Idle", new VizRect(hudBounds.Left, hudBounds.Top + 18, 240, 16), scene.Theme.HudInfoColor, 12, VizTextAlign.Left);

        canvas.FillRect(meterRect, new VizColor(42, 255, 245, 230));
        canvas.FillRectGradientH(
            new VizRect(meterRect.Left, meterRect.Top, Math.Max(6, meterRect.Width * Math.Clamp(scene.PeakLevel, 0, 1)), meterRect.Height),
            scene.Theme.BarStartColor,
            scene.Theme.BarEndColor);
        canvas.DrawText(
            $"Peak {(int)(Math.Clamp(scene.PeakLevel, 0, 1) * 100)}%",
            new VizRect(meterRect.Left - 64, meterRect.Top - 6, 60, 16),
            scene.Theme.HudInfoColor,
            12,
            VizTextAlign.Left);
    }

    protected static void DrawPlaceholder(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        canvas.DrawText(
            "Drop audio here or use Open to start playback",
            new VizRect(bounds.Left, bounds.CenterY - 10, bounds.Width, 20),
            scene.Theme.PlaceholderColor,
            12,
            VizTextAlign.Center);
    }

    protected static bool IsNearSilence(VisualizerScene scene) =>
        scene.SpectrumLevels.All(level => level < 0.01f) &&
        scene.WaveformPoints.All(point => Math.Abs(point) < 0.01f);

    protected static float SampleRange(float[] source, int index, int displayBars) =>
        VizMath.SampleRange(source, index, displayBars);
}
