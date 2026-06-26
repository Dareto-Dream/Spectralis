using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

internal sealed class KaraokeDisplayControl : Control
{
    private LyricsDocument? _doc;
    private double _position;
    private int _lineIdx = -1;

    private static readonly Font ActiveFont = new("Segoe UI Semibold", 40f, FontStyle.Bold, GraphicsUnit.Point);
    private static readonly Font ContextFont = new("Segoe UI", 24f, FontStyle.Regular, GraphicsUnit.Point);
    private static readonly StringFormat CenterSF = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap,
    };

    public KaraokeDisplayControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.Black;
        ForeColor = Color.White;
    }

    public void SetDocument(LyricsDocument? doc)
    {
        _doc = doc;
        _position = 0;
        _lineIdx = -1;
        Invalidate();
    }

    public void SetPosition(double posSeconds)
    {
        _position = posSeconds;
        var newIdx = _doc?.FindLineIndex(posSeconds) ?? -1;
        _lineIdx = newIdx;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.Black);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (_doc is null || _doc.Lines.Count == 0)
        {
            using var msgBrush = new SolidBrush(Color.FromArgb(80, 255, 255, 255));
            g.DrawString("No lyrics available", ContextFont, msgBrush,
                new RectangleF(0, 0, Width, Height), CenterSF);
            return;
        }

        var w = Width;
        var h = Height;
        var centerY = h / 2f;
        var idx = _lineIdx;
        var lines = _doc.Lines;

        const float SidePad = 60f;
        float contentW = w - SidePad * 2;

        // Previous line (dim, above center)
        if (idx > 0)
        {
            var rect = new RectangleF(SidePad, centerY - 110f, contentW, 52f);
            DrawContextLine(g, lines[idx - 1].Text, ContextFont, rect, 70);
        }

        // Current active line (center)
        if (idx >= 0 && idx < lines.Count)
        {
            var rect = new RectangleF(SidePad, centerY - 46f, contentW, 92f);
            DrawActiveLine(g, lines[idx], rect);
        }
        else if (idx < 0 && lines.Count > 0)
        {
            // Before first lyric — show first line dimly
            var rect = new RectangleF(SidePad, centerY - 46f, contentW, 92f);
            DrawContextLine(g, lines[0].Text, ActiveFont, rect, 40);
        }

        // Next line (dim, below center)
        int nextIdx = idx + 1;
        if (nextIdx >= 0 && nextIdx < lines.Count)
        {
            var rect = new RectangleF(SidePad, centerY + 58f, contentW, 52f);
            DrawContextLine(g, lines[nextIdx].Text, ContextFont, rect, 70);
        }
    }

    private static void DrawContextLine(Graphics g, string text, Font font, RectangleF rect, int alpha)
    {
        using var brush = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255));
        g.DrawString(text, font, brush, rect, CenterSF);
    }

    private void DrawActiveLine(Graphics g, LyricsLine line, RectangleF rect)
    {
        var segs = line.Segments;

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
            PaintFilledText(g, line.Text, ActiveFont, rect, frac);
            return;
        }

        // Syllable-level fill: measure each syllable, center the group, wipe each one
        var widths = new float[segs.Count];
        float totalW = 0f;
        for (int i = 0; i < segs.Count; i++)
        {
            widths[i] = g.MeasureString(segs[i].Text, ActiveFont).Width;
            totalW += widths[i];
        }

        float scale = totalW > rect.Width ? rect.Width / totalW : 1f;
        float x = rect.Left + (rect.Width - totalW * scale) / 2f;

        for (int i = 0; i < segs.Count; i++)
        {
            double segStart = segs[i].StartTime;
            double segEnd = i + 1 < segs.Count ? segs[i + 1].StartTime : segStart + 1.5;
            float frac;
            if (_position < segStart)     frac = 0f;
            else if (_position >= segEnd) frac = 1f;
            else frac = (float)((_position - segStart) / (segEnd - segStart));

            float segW = widths[i] * scale;
            var segRect = new RectangleF(x, rect.Top, segW, rect.Height);
            PaintFilledText(g, segs[i].Text, ActiveFont, segRect, frac);
            x += segW;
        }
    }

    private static void PaintFilledText(Graphics g, string text, Font font, RectangleF rect, float fillFrac)
    {
        // Dim base (white at low opacity)
        using var dimBrush = new SolidBrush(Color.FromArgb(110, 255, 255, 255));
        g.DrawString(text, font, dimBrush, rect, CenterSF);

        if (fillFrac <= 0f) return;

        // Left-to-right wipe in gold
        var state = g.Save();
        g.SetClip(new RectangleF(rect.Left, rect.Top, rect.Width * fillFrac, rect.Height));
        using var fillBrush = new SolidBrush(Color.FromArgb(255, 255, 215, 55));
        g.DrawString(text, font, fillBrush, rect, CenterSF);
        g.Restore(state);
    }
}
