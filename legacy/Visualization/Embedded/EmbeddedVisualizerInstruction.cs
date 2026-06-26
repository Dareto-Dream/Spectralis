using System.Drawing;

namespace Spectralis;

internal abstract record EmbeddedDrawInstruction;

internal sealed record EmbeddedLineInstruction(
    float X1,
    float Y1,
    float X2,
    float Y2,
    Color Color,
    float Thickness) : EmbeddedDrawInstruction;

internal sealed record EmbeddedRectangleInstruction(
    float X,
    float Y,
    float Width,
    float Height,
    Color Color,
    float Thickness,
    bool Filled) : EmbeddedDrawInstruction;

internal sealed record EmbeddedCircleInstruction(
    float CenterX,
    float CenterY,
    float Radius,
    Color Color,
    float Thickness,
    bool Filled) : EmbeddedDrawInstruction;
