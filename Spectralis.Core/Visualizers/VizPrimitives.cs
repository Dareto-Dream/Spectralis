using System.Numerics;

namespace Spectralis.Core.Visualizers;

/// <summary>ARGB color, render-toolkit agnostic.</summary>
public readonly record struct VizColor(byte A, byte R, byte G, byte B)
{
    public static VizColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public VizColor WithAlpha(int alpha) => this with { A = (byte)Math.Clamp(alpha, 0, 255) };

    public static VizColor Blend(VizColor start, VizColor end, float amount)
    {
        var mix = Math.Clamp(amount, 0f, 1f);
        return new VizColor(
            (byte)(start.A + ((end.A - start.A) * mix)),
            (byte)(start.R + ((end.R - start.R) * mix)),
            (byte)(start.G + ((end.G - start.G) * mix)),
            (byte)(start.B + ((end.B - start.B) * mix)));
    }
}

public readonly record struct VizRect(float X, float Y, float Width, float Height)
{
    public float Left => X;
    public float Top => Y;
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public float CenterX => X + (Width / 2f);
    public float CenterY => Y + (Height / 2f);

    public VizRect Inflate(float dx, float dy) =>
        new(X - dx, Y - dy, Width + (2 * dx), Height + (2 * dy));

    public static VizRect FromLTRB(float left, float top, float right, float bottom) =>
        new(left, top, right - left, bottom - top);
}

/// <summary>Opaque image handle; the rendering layer knows the concrete type.</summary>
public interface IVizImage
{
    float Width { get; }
    float Height { get; }
}

public enum VizTextAlign
{
    Left,
    Center,
    Right,
}
