using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

internal sealed class EmbeddedVisualizerRenderer : VisualizerRendererBase
{
    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        Draw(graphics, bounds, scene, Array.Empty<EmbeddedDrawInstruction>());
    }

    public void Draw(
        Graphics graphics,
        Rectangle bounds,
        VisualizerScene scene,
        IReadOnlyList<EmbeddedDrawInstruction> instructions)
    {
        DrawBackground(graphics, bounds, scene);
        DrawGrid(graphics, bounds, scene);
        DrawInstructions(graphics, bounds, instructions);
        DrawHud(graphics, bounds, scene);

        if (instructions.Count == 0)
        {
            DrawPlaceholder(graphics, bounds, scene);
        }
    }

    private static void DrawInstructions(
        Graphics graphics,
        Rectangle bounds,
        IReadOnlyList<EmbeddedDrawInstruction> instructions)
    {
        var contentBounds = RectangleF.FromLTRB(
            bounds.Left + 22f,
            bounds.Top + 62f,
            bounds.Right - 22f,
            bounds.Bottom - 22f);

        if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
        {
            return;
        }

        foreach (var instruction in instructions)
        {
            switch (instruction)
            {
                case EmbeddedLineInstruction line:
                    DrawLine(graphics, contentBounds, line);
                    break;
                case EmbeddedRectangleInstruction rectangle:
                    DrawRectangle(graphics, contentBounds, rectangle);
                    break;
                case EmbeddedCircleInstruction circle:
                    DrawCircle(graphics, contentBounds, circle);
                    break;
            }
        }
    }

    private static void DrawLine(Graphics graphics, RectangleF contentBounds, EmbeddedLineInstruction line)
    {
        using var pen = CreatePen(line.Color, line.Thickness);
        graphics.DrawLine(
            pen,
            MapX(contentBounds, line.X1),
            MapY(contentBounds, line.Y1),
            MapX(contentBounds, line.X2),
            MapY(contentBounds, line.Y2));
    }

    private static void DrawRectangle(
        Graphics graphics,
        RectangleF contentBounds,
        EmbeddedRectangleInstruction rectangle)
    {
        var bounds = RectangleF.FromLTRB(
            MapX(contentBounds, rectangle.X),
            MapY(contentBounds, rectangle.Y),
            MapX(contentBounds, rectangle.X + rectangle.Width),
            MapY(contentBounds, rectangle.Y + rectangle.Height));

        if (rectangle.Filled)
        {
            using var brush = new SolidBrush(rectangle.Color);
            graphics.FillRectangle(brush, NormalizeRect(bounds));
            return;
        }

        using var pen = CreatePen(rectangle.Color, rectangle.Thickness);
        graphics.DrawRectangle(pen, NormalizeRect(bounds));
    }

    private static void DrawCircle(Graphics graphics, RectangleF contentBounds, EmbeddedCircleInstruction circle)
    {
        var radius = Math.Max(0.5f, Math.Min(contentBounds.Width, contentBounds.Height) * Math.Clamp(circle.Radius, 0, 1));
        var centerX = MapX(contentBounds, circle.CenterX);
        var centerY = MapY(contentBounds, circle.CenterY);
        var ellipseBounds = new RectangleF(centerX - radius, centerY - radius, radius * 2, radius * 2);

        if (circle.Filled)
        {
            using var brush = new SolidBrush(circle.Color);
            graphics.FillEllipse(brush, ellipseBounds);
            return;
        }

        using var pen = CreatePen(circle.Color, circle.Thickness);
        graphics.DrawEllipse(pen, ellipseBounds);
    }

    private static Pen CreatePen(Color color, float thickness) =>
        new(color, Math.Max(1f, thickness))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

    private static RectangleF NormalizeRect(RectangleF rectangle) =>
        RectangleF.FromLTRB(
            Math.Min(rectangle.Left, rectangle.Right),
            Math.Min(rectangle.Top, rectangle.Bottom),
            Math.Max(rectangle.Left, rectangle.Right),
            Math.Max(rectangle.Top, rectangle.Bottom));

    private static float MapX(RectangleF bounds, float normalizedX) =>
        bounds.Left + (Math.Clamp(normalizedX, 0, 1) * bounds.Width);

    private static float MapY(RectangleF bounds, float normalizedY) =>
        bounds.Top + (Math.Clamp(normalizedY, 0, 1) * bounds.Height);
}
