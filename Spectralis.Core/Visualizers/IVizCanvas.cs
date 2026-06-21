using System.Numerics;

namespace Spectralis.Core.Visualizers;

/// <summary>
/// Render-toolkit-agnostic drawing surface for visualizers. The Avalonia app
/// implements this over DrawingContext; tests use a no-op implementation, and
/// the OBS overlay or video export can implement it over raster targets.
/// Angles are degrees, GDI-style (clockwise from +X).
/// </summary>
public interface IVizCanvas
{
    void FillRect(VizRect rect, VizColor color);
    void FillRectGradientV(VizRect rect, VizColor top, VizColor bottom);
    void FillRectGradientH(VizRect rect, VizColor left, VizColor right);

    /// <summary>Soft elliptical glow: solid at center fading to transparent at the edge.</summary>
    void FillRadialGlow(VizRect rect, VizColor center);

    void DrawLine(Vector2 start, Vector2 end, VizColor color, float width, bool roundCap = false);
    void DrawPolyline(ReadOnlySpan<Vector2> points, VizColor color, float width, bool roundCap = false);
    void FillPolygon(ReadOnlySpan<Vector2> points, VizColor color);
    void DrawPolygon(ReadOnlySpan<Vector2> points, VizColor color, float width);

    void FillEllipse(VizRect rect, VizColor color);
    void DrawEllipse(VizRect rect, VizColor color, float width);

    void FillRoundedRect(VizRect rect, float radius, VizColor color);
    void FillRoundedRectGradientV(VizRect rect, float radius, VizColor top, VizColor bottom);
    void DrawRoundedRect(VizRect rect, float radius, VizColor color, float width);

    void DrawArc(VizRect rect, float startAngleDeg, float sweepDeg, VizColor color, float width);

    void DrawText(string text, VizRect rect, VizColor color, float fontSize, VizTextAlign align, bool bold = false);

    void DrawImage(IVizImage image, VizRect dest);

    /// <summary>Blits a raw BGRA pixel buffer scaled into <paramref name="dest"/> (spectrogram path).</summary>
    void DrawPixels(byte[] bgra, int pixelWidth, int pixelHeight, VizRect dest);

    /// <summary>Pushes an elliptical clip. Pair with <see cref="Restore"/>.</summary>
    void PushClipEllipse(VizRect rect);

    /// <summary>Pushes a rectangular clip. Pair with <see cref="Restore"/>.</summary>
    void PushClipRect(VizRect rect);

    /// <summary>Pushes a rounded-rectangle clip. Pair with <see cref="Restore"/>.</summary>
    void PushClipRoundedRect(VizRect rect, float radius);

    /// <summary>Pushes translate+rotate about the given point. Pair with <see cref="Restore"/>.</summary>
    void PushRotation(float angleDeg, Vector2 center);

    void Restore();
}
