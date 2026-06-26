using System.Numerics;
using Spectralis.Core.Visualizers;

namespace Spectralis.Tests.Core;

/// <summary>No-op canvas: exercises renderer math without a rendering toolkit.</summary>
public sealed class NullVizCanvas : IVizCanvas
{
    public int CallCount { get; private set; }

    private void Count() => CallCount++;

    public void FillRect(VizRect rect, VizColor color) => Count();
    public void FillRectGradientV(VizRect rect, VizColor top, VizColor bottom) => Count();
    public void FillRectGradientH(VizRect rect, VizColor left, VizColor right) => Count();
    public void FillRadialGlow(VizRect rect, VizColor center) => Count();
    public void DrawLine(Vector2 start, Vector2 end, VizColor color, float width, bool roundCap = false) => Count();
    public void DrawPolyline(ReadOnlySpan<Vector2> points, VizColor color, float width, bool roundCap = false) => Count();
    public void FillPolygon(ReadOnlySpan<Vector2> points, VizColor color) => Count();
    public void DrawPolygon(ReadOnlySpan<Vector2> points, VizColor color, float width) => Count();
    public void FillEllipse(VizRect rect, VizColor color) => Count();
    public void DrawEllipse(VizRect rect, VizColor color, float width) => Count();
    public void FillRoundedRect(VizRect rect, float radius, VizColor color) => Count();
    public void FillRoundedRectGradientV(VizRect rect, float radius, VizColor top, VizColor bottom) => Count();
    public void DrawArc(VizRect rect, float startAngleDeg, float sweepDeg, VizColor color, float width) => Count();
    public void DrawText(string text, VizRect rect, VizColor color, float fontSize, VizTextAlign align, bool bold = false) => Count();
    public void DrawImage(IVizImage image, VizRect dest) => Count();
    public void DrawPixels(byte[] bgra, int pixelWidth, int pixelHeight, VizRect dest) => Count();
    public void DrawRoundedRect(VizRect rect, float radius, VizColor color, float width) => Count();
    public void PushClipEllipse(VizRect rect) => Count();
    public void PushClipRect(VizRect rect) => Count();
    public void PushClipRoundedRect(VizRect rect, float radius) => Count();
    public void PushRotation(float angleDeg, Vector2 center) => Count();
    public void Restore() => Count();
}
