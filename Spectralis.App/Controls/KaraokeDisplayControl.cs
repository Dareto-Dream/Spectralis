using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Spectralis.Core.Lyrics;

namespace Spectralis.App.Controls;

/// <summary>
/// Full-surface karaoke renderer: previous/current/next lyric lines with
/// left-to-right gold fill wipe on the active line (line-level or syllable-level).
/// </summary>
public sealed class KaraokeDisplayControl : Control
{
    private LyricsDocument? _doc;
    private double _position;
    private int _lineIdx = -1;

    private static readonly Typeface ActiveTypeface = new(
        new FontFamily("Segoe UI, Arial, sans-serif"), FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface ContextTypeface = new(
        new FontFamily("Segoe UI, Arial, sans-serif"), FontStyle.Normal, FontWeight.Normal);

    private const double ActiveFontSize = 40;
    private const double ContextFontSize = 24;

    public void SetDocument(LyricsDocument? doc)
    {
        _doc = doc;
        _position = 0;
        _lineIdx = -1;
        InvalidateVisual();
    }

    public void SetPosition(double posSeconds)
    {
        _position = posSeconds;
        _lineIdx = _doc?.FindLineIndex(posSeconds) ?? -1;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;

        ctx.FillRectangle(Brushes.Black, new Rect(0, 0, w, h));

        if (_doc is null || _doc.Lines.Count == 0)
        {
            var ft = MakeText("No lyrics available", ContextTypeface, ContextFontSize,
                new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)));
            ctx.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
            return;
        }

        const double SidePad = 60;
        double contentW = w - SidePad * 2;
        double centerY = h / 2;
        var idx = _lineIdx;
        var lines = _doc.Lines;
        var contextBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));

        // Previous line (dim, above center)
        if (idx > 0)
        {
            DrawContextLine(ctx, lines[idx - 1].Text, contextBrush,
                SidePad, centerY - 100, contentW, 48);
        }

        // Current active line (center)
        if (idx >= 0 && idx < lines.Count)
        {
            DrawActiveLine(ctx, lines[idx], SidePad, centerY - 46, contentW, 92);
        }
        else if (idx < 0 && lines.Count > 0)
        {
            // Before first lyric — show first line dimly
            DrawContextLine(ctx, lines[0].Text,
                new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                SidePad, centerY - 46, contentW, 92);
        }

        // Next line (dim, below center)
        int nextIdx = idx + 1;
        if (nextIdx >= 0 && nextIdx < lines.Count)
        {
            DrawContextLine(ctx, lines[nextIdx].Text, contextBrush,
                SidePad, centerY + 56, contentW, 48);
        }
    }

    private static void DrawContextLine(DrawingContext ctx, string text, IBrush brush,
        double x, double y, double width, double height)
    {
        var ft = MakeText(text, ContextTypeface, ContextFontSize, brush);
        ctx.DrawText(ft, new Point(x + Math.Max(0, (width - ft.Width) / 2), y + (height - ft.Height) / 2));
    }

    private void DrawActiveLine(DrawingContext ctx, LyricsLine line,
        double x, double y, double width, double height)
    {
        var segs = line.Segments;
        var dimBrush = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));

        if (segs.Count <= 1)
        {
            // Line-level fill: fraction of time elapsed within this line's window
            var lines = _doc!.Lines;
            double lineStart = line.StartTime;
            double lineEnd = _lineIdx + 1 < lines.Count
                ? lines[_lineIdx + 1].StartTime
                : lineStart + 5.0;
            float frac = lineEnd > lineStart
                ? Math.Clamp((float)((_position - lineStart) / (lineEnd - lineStart)), 0f, 1f)
                : 1f;
            PaintFilledText(ctx, line.Text, ActiveFontSize, x, y, width, height, frac);
            return;
        }

        // Syllable-level fill: measure at full size, scale font down if overflow
        var naturalWidths = segs
            .Select(seg => MakeText(seg.Text, ActiveTypeface, ActiveFontSize, dimBrush).Width)
            .ToArray();
        double totalW = naturalWidths.Sum();
        double scale = totalW > width ? width / totalW : 1.0;
        double fontSize = ActiveFontSize * scale;

        double[] widths = naturalWidths.Select(nw => nw * scale).ToArray();
        double effectiveTotalW = widths.Sum();
        double curX = x + (width - effectiveTotalW) / 2;

        for (int i = 0; i < segs.Count; i++)
        {
            double segStart = segs[i].StartTime;
            double segEnd = i + 1 < segs.Count ? segs[i + 1].StartTime : segStart + 1.5;
            float frac;
            if (_position < segStart)     frac = 0f;
            else if (_position >= segEnd) frac = 1f;
            else frac = (float)((_position - segStart) / (segEnd - segStart));

            PaintFilledText(ctx, segs[i].Text, fontSize, curX, y, widths[i], height, frac);
            curX += widths[i];
        }
    }

    private static void PaintFilledText(DrawingContext ctx, string text, double fontSize,
        double x, double y, double width, double height, float fillFrac)
    {
        var dimBrush = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));
        var ft = MakeText(text, ActiveTypeface, fontSize, dimBrush);
        double textX = x + (width - ft.Width) / 2;
        double textY = y + (height - ft.Height) / 2;
        ctx.DrawText(ft, new Point(textX, textY));

        if (fillFrac <= 0f) return;

        var goldBrush = new SolidColorBrush(Color.FromRgb(255, 215, 55));
        var ftGold = MakeText(text, ActiveTypeface, fontSize, goldBrush);
        using (ctx.PushClip(new Rect(x, y, width * fillFrac, height)))
        {
            ctx.DrawText(ftGold, new Point(textX, textY));
        }
    }

    private static FormattedText MakeText(string text, Typeface typeface, double size, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);
}
