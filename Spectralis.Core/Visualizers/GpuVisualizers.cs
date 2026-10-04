namespace Spectralis.Core.Visualizers;

/// <summary>
/// Renders one frame of a GPU visualizer. Implemented by the app (it owns the native GPU library); Core only
/// knows this shape, so the catalog and renderers stay cross-platform and testable.
/// </summary>
public interface IGpuFrameSource
{
    /// <summary>False when the GPU library or a usable adapter isn't there. May flip to false after a failed first frame.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Renders built-in visualizer <paramref name="builtinIndex"/> at <paramref name="width"/>x<paramref name="height"/>
    /// and returns tightly-packed BGRA8 pixels, or null if this frame couldn't be produced.
    /// </summary>
    byte[]? RenderFrame(
        int builtinIndex, int width, int height, double timeSeconds,
        ReadOnlySpan<float> spectrum, float rms, float peak, VizColor accent);
}

/// <summary>Where the app plugs in its GPU frame source at startup. Null means no GPU visualizers.</summary>
public static class GpuVisualizers
{
    public static IGpuFrameSource? Source { get; set; }

    public static bool IsAvailable => Source is { IsAvailable: true };
}

/// <summary>
/// Draws a built-in GPU visualizer through the canvas's pixel blit. If the GPU can't produce a frame (no
/// library, no adapter, a dropped frame) it draws <paramref name="fallback"/> instead, so a saved GPU mode
/// on a machine without one still shows something sensible, never a blank screen.
/// </summary>
public sealed class GpuVisualizerRenderer : IVisualizerRenderer
{
    /// <summary>Rendering above this width gains nothing on screen and costs a lot in readback; the blit scales it up.</summary>
    public const int MaxRenderWidth = 1280;

    private readonly int _builtinIndex;
    private readonly IVisualizerRenderer _fallback;

    public GpuVisualizerRenderer(int builtinIndex, IVisualizerRenderer fallback)
    {
        _builtinIndex = builtinIndex;
        _fallback = fallback;
    }

    public void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var source = GpuVisualizers.Source;
        if (source is not { IsAvailable: true })
        {
            _fallback.Draw(canvas, bounds, scene);
            return;
        }

        var (width, height) = ChooseRenderSize(bounds.Width, bounds.Height, MaxRenderWidth);
        var pixels = source.RenderFrame(
            _builtinIndex, width, height, scene.PlaybackTimeSeconds,
            scene.SpectrumLevels, scene.RmsLevel, scene.PeakLevel, scene.Theme.BarStartColor);

        if (pixels is null || pixels.Length != width * height * 4)
        {
            _fallback.Draw(canvas, bounds, scene);
            return;
        }

        canvas.DrawPixels(pixels, width, height, bounds);
    }

    /// <summary>The pixel size to render for a drawing area: its own aspect ratio, capped at <paramref name="maxWidth"/>, never below 2px.</summary>
    public static (int Width, int Height) ChooseRenderSize(float areaWidth, float areaHeight, int maxWidth)
    {
        if (!float.IsFinite(areaWidth) || !float.IsFinite(areaHeight) || areaWidth < 1 || areaHeight < 1)
        {
            return (2, 2);
        }

        var scale = Math.Min(1f, maxWidth / areaWidth);
        var width = Math.Max(2, (int)MathF.Round(areaWidth * scale));
        var height = Math.Max(2, (int)MathF.Round(areaHeight * scale));
        return (width, height);
    }
}
