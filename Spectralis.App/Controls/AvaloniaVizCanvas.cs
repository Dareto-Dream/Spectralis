using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Controls;

/// <summary>Wraps an Avalonia bitmap as the opaque image handle renderers carry.</summary>
public sealed class AvaloniaVizImage : IVizImage
{
    public AvaloniaVizImage(IImage image) => Image = image;

    public IImage Image { get; }
    public float Width => (float)Image.Size.Width;
    public float Height => (float)Image.Size.Height;

    public static AvaloniaVizImage? FromBytes(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            return new AvaloniaVizImage(new Bitmap(stream));
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>IVizCanvas implementation over Avalonia's DrawingContext.</summary>
public sealed class AvaloniaVizCanvas : IVizCanvas
{
    private static readonly Typeface UiTypeface = new("Segoe UI Variable Text, Segoe UI, Inter, sans-serif");
    private static readonly Typeface UiTypefaceBold = new(UiTypeface.FontFamily, FontStyle.Normal, FontWeight.Bold);

    private readonly DrawingContext _context;
    private readonly Stack<DrawingContext.PushedState> _states = new();

    public AvaloniaVizCanvas(DrawingContext context) => _context = context;

    public void FillRect(VizRect rect, VizColor color) =>
        _context.DrawRectangle(Brush(color), null, ToRect(rect));

    public void FillRectGradientV(VizRect rect, VizColor top, VizColor bottom) =>
        _context.DrawRectangle(LinearBrush(top, bottom, vertical: true), null, ToRect(rect));

    public void FillRectGradientH(VizRect rect, VizColor left, VizColor right) =>
        _context.DrawRectangle(LinearBrush(left, right, vertical: false), null, ToRect(rect));

    public void FillRadialGlow(VizRect rect, VizColor center)
    {
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(ToColor(center), 0),
                new GradientStop(ToColor(center.WithAlpha(0)), 1),
            },
        };
        _context.DrawEllipse(brush, null, new Point(rect.CenterX, rect.CenterY), rect.Width / 2, rect.Height / 2);
    }

    public void DrawLine(Vector2 start, Vector2 end, VizColor color, float width, bool roundCap = false) =>
        _context.DrawLine(MakePen(color, width, roundCap), ToPoint(start), ToPoint(end));

    public void DrawPolyline(ReadOnlySpan<Vector2> points, VizColor color, float width, bool roundCap = false)
    {
        if (points.Length < 2)
        {
            return;
        }

        _context.DrawGeometry(null, MakePen(color, width, roundCap), BuildPolyGeometry(points, closed: false));
    }

    public void FillPolygon(ReadOnlySpan<Vector2> points, VizColor color)
    {
        if (points.Length < 3)
        {
            return;
        }

        _context.DrawGeometry(Brush(color), null, BuildPolyGeometry(points, closed: true));
    }

    public void DrawPolygon(ReadOnlySpan<Vector2> points, VizColor color, float width)
    {
        if (points.Length < 2)
        {
            return;
        }

        _context.DrawGeometry(null, MakePen(color, width, roundCap: false), BuildPolyGeometry(points, closed: true));
    }

    public void FillEllipse(VizRect rect, VizColor color) =>
        _context.DrawEllipse(Brush(color), null, new Point(rect.CenterX, rect.CenterY), rect.Width / 2, rect.Height / 2);

    public void DrawEllipse(VizRect rect, VizColor color, float width) =>
        _context.DrawEllipse(null, MakePen(color, width, roundCap: false), new Point(rect.CenterX, rect.CenterY), rect.Width / 2, rect.Height / 2);

    public void FillRoundedRect(VizRect rect, float radius, VizColor color) =>
        _context.DrawRectangle(Brush(color), null, ToRect(rect), radius, radius);

    public void FillRoundedRectGradientV(VizRect rect, float radius, VizColor top, VizColor bottom) =>
        _context.DrawRectangle(LinearBrush(top, bottom, vertical: true), null, ToRect(rect), radius, radius);

    public void DrawRoundedRect(VizRect rect, float radius, VizColor color, float width) =>
        _context.DrawRectangle(null, MakePen(color, width, roundCap: false), ToRect(rect), radius, radius);

    public void DrawArc(VizRect rect, float startAngleDeg, float sweepDeg, VizColor color, float width)
    {
        var cx = rect.CenterX;
        var cy = rect.CenterY;
        var rx = rect.Width / 2;
        var ry = rect.Height / 2;

        var startRad = startAngleDeg * MathF.PI / 180f;
        var endRad = (startAngleDeg + sweepDeg) * MathF.PI / 180f;

        var startPoint = new Point(cx + (rx * Math.Cos(startRad)), cy + (ry * Math.Sin(startRad)));
        var endPoint = new Point(cx + (rx * Math.Cos(endRad)), cy + (ry * Math.Sin(endRad)));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(startPoint, isFilled: false);
            ctx.ArcTo(
                endPoint,
                new Size(rx, ry),
                0,
                Math.Abs(sweepDeg) > 180,
                sweepDeg >= 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise);
            ctx.EndFigure(false);
        }

        _context.DrawGeometry(null, MakePen(color, width, roundCap: true), geometry);
    }

    public void DrawText(string text, VizRect rect, VizColor color, float fontSize, VizTextAlign align, bool bold = false)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            bold ? UiTypefaceBold : UiTypeface,
            fontSize,
            Brush(color))
        {
            MaxTextWidth = Math.Max(1, rect.Width),
            TextAlignment = align switch
            {
                VizTextAlign.Center => TextAlignment.Center,
                VizTextAlign.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            },
        };

        _context.DrawText(formatted, new Point(rect.X, rect.Y));
    }

    public void DrawImage(IVizImage image, VizRect dest)
    {
        if (image is not AvaloniaVizImage avaloniaImage)
        {
            return;
        }

        var source = new Rect(avaloniaImage.Image.Size);
        _context.DrawImage(avaloniaImage.Image, source, ToRect(dest));
    }

    public void DrawPixels(byte[] bgra, int pixelWidth, int pixelHeight, VizRect dest)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0 || bgra.Length < pixelWidth * pixelHeight * 4)
        {
            return;
        }

        var bitmap = RentPixelBitmap(pixelWidth, pixelHeight);
        using (var buffer = bitmap.Lock())
        {
            System.Runtime.InteropServices.Marshal.Copy(bgra, 0, buffer.Address, pixelWidth * pixelHeight * 4);
        }

        _context.DrawImage(bitmap, new Rect(0, 0, pixelWidth, pixelHeight), ToRect(dest));
    }

    // The canvas is recreated per frame around the DrawingContext, so the pixel
    // surface for DrawPixels is cached process-wide and reused while the size holds.
    private static WriteableBitmap? s_pixelBitmap;

    private static WriteableBitmap RentPixelBitmap(int width, int height)
    {
        if (s_pixelBitmap is null ||
            (int)s_pixelBitmap.PixelSize.Width != width ||
            (int)s_pixelBitmap.PixelSize.Height != height)
        {
            s_pixelBitmap?.Dispose();
            s_pixelBitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Avalonia.Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Premul);
        }

        return s_pixelBitmap;
    }

    public void PushClipEllipse(VizRect rect) =>
        _states.Push(_context.PushGeometryClip(new EllipseGeometry(ToRect(rect))));

    public void PushClipRect(VizRect rect) =>
        _states.Push(_context.PushClip(ToRect(rect)));

    public void PushClipRoundedRect(VizRect rect, float radius) =>
        _states.Push(_context.PushClip(new RoundedRect(ToRect(rect), radius)));

    public void PushRotation(float angleDeg, Vector2 center)
    {
        var radians = angleDeg * Math.PI / 180.0;
        var matrix =
            Matrix.CreateTranslation(-center.X, -center.Y) *
            Matrix.CreateRotation(radians) *
            Matrix.CreateTranslation(center.X, center.Y);
        _states.Push(_context.PushTransform(matrix));
    }

    public void Restore()
    {
        if (_states.Count > 0)
        {
            _states.Pop().Dispose();
        }
    }

    private static StreamGeometry BuildPolyGeometry(ReadOnlySpan<Vector2> points, bool closed)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        ctx.BeginFigure(ToPoint(points[0]), isFilled: closed);
        for (var i = 1; i < points.Length; i++)
        {
            ctx.LineTo(ToPoint(points[i]));
        }

        ctx.EndFigure(closed);
        return geometry;
    }

    private static Pen MakePen(VizColor color, float width, bool roundCap) =>
        new(Brush(color), width)
        {
            LineCap = roundCap ? PenLineCap.Round : PenLineCap.Flat,
            LineJoin = PenLineJoin.Round,
        };

    private static LinearGradientBrush LinearBrush(VizColor start, VizColor end, bool vertical) =>
        new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = vertical
                ? new RelativePoint(0, 1, RelativeUnit.Relative)
                : new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(ToColor(start), 0),
                new GradientStop(ToColor(end), 1),
            },
        };

    private static SolidColorBrush Brush(VizColor color) => new(ToColor(color));

    private static Color ToColor(VizColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    private static Rect ToRect(VizRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Point ToPoint(Vector2 point) => new(point.X, point.Y);
}
