using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Spectralis.Core.Scrobbling;

namespace Spectralis.App.Stats;

/// <summary>Paints a <see cref="WrappedSummary"/> as a shareable PNG (story-sized by default).</summary>
public static class WrappedCardRenderer
{
    private static readonly Typeface Regular = new("Segoe UI, Inter, sans-serif");
    private static readonly Typeface Bold = new(
        new FontFamily("Segoe UI, Inter, sans-serif"), FontStyle.Normal, FontWeight.Bold);

    private static readonly Color Top = Color.Parse("#1b1030");
    private static readonly Color Bottom = Color.Parse("#0a2a3a");
    private static readonly Color Accent = Color.Parse("#7cf2c8");

    public static byte[] RenderPng(WrappedSummary summary, int width = 1080, int height = 1920)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
            Draw(ctx, summary, width, height);

        using var ms = new MemoryStream();
        bitmap.Save(ms);
        return ms.ToArray();
    }

    private static void Draw(DrawingContext ctx, WrappedSummary s, int width, int height)
    {
        var scale = width / 1080.0;
        var margin = 90 * scale;
        var textWidth = width - (margin * 2);

        var bg = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Top, 0), new GradientStop(Bottom, 1)],
        };
        ctx.DrawRectangle(bg, null, new Rect(0, 0, width, height));

        var y = 110 * scale;
        y = Line(ctx, "YOUR SPECTRALIS", Bold, 38 * scale, new SolidColorBrush(Accent), margin, y, textWidth) + (6 * scale);
        y = Line(ctx, s.PeriodLabel, Regular, 46 * scale, Brushes.White, margin, y, textWidth) + (50 * scale);

        if (!s.HasData)
        {
            Line(ctx, "No listening history yet. Play something!", Regular, 52 * scale, Brushes.White, margin, y, textWidth);
            return;
        }

        y = Line(ctx, s.Scrobbles.ToString("N0", CultureInfo.CurrentCulture), Bold, 190 * scale, Brushes.White, margin, y, textWidth);
        y = Line(ctx, "plays", Regular, 44 * scale, Brushes.Gray, margin, y - (10 * scale), textWidth) + (30 * scale);
        y = Line(ctx, $"{s.HoursText} hours  ·  best streak {s.LongestStreakDays} days", Regular, 44 * scale,
            new SolidColorBrush(Accent), margin, y, textWidth) + (80 * scale);

        y = List(ctx, "TOP ARTISTS", s.TopArtists, scale, margin, y, textWidth) + (60 * scale);
        List(ctx, "TOP TRACKS", s.TopTracks, scale, margin, y, textWidth);

        Line(ctx, "made with Spectralis", Regular, 34 * scale, Brushes.Gray, margin, height - (110 * scale), textWidth);
    }

    private static double List(
        DrawingContext ctx, string title, IReadOnlyList<WrappedEntry> entries,
        double scale, double x, double y, double maxWidth)
    {
        y = Line(ctx, title, Bold, 36 * scale, new SolidColorBrush(Accent), x, y, maxWidth) + (14 * scale);
        foreach (var e in entries)
            y = Line(ctx, $"{e.Rank}. {e.Name}", Regular, 46 * scale, Brushes.White, x, y, maxWidth) + (8 * scale);
        return y;
    }

    /// <summary>Draws one trimmed line and returns the y below it.</summary>
    private static double Line(
        DrawingContext ctx, string text, Typeface face, double size, IBrush brush,
        double x, double y, double maxWidth)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        ctx.DrawText(ft, new Point(x, y));
        return y + ft.Height;
    }
}
