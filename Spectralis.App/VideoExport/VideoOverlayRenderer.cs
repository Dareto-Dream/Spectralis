using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Spectralis.App.VideoExport;

/// <summary>What to paint over an exported frame — the enabled overlay pieces plus their data.</summary>
public sealed class VideoOverlayModel
{
    public bool ShowTitle { get; init; }
    public bool ShowArtist { get; init; }
    public bool ShowAlbum { get; init; }
    public bool ShowAlbumArt { get; init; }
    public bool ShowProgressBar { get; init; }

    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public Bitmap? Cover { get; init; }

    public double ElapsedSeconds { get; init; }
    public double TotalSeconds { get; init; }

    public bool HasAnyText => (ShowTitle && !string.IsNullOrWhiteSpace(Title))
        || (ShowArtist && !string.IsNullOrWhiteSpace(Artist))
        || (ShowAlbum && !string.IsNullOrWhiteSpace(Album));

    public bool HasAnything => ShowProgressBar || HasAnyText || (ShowAlbumArt && Cover is not null);
}

/// <summary>
/// Draws the export overlay — bottom-left rounded text card, bottom-right rounded album
/// cover, thin bottom progress bar — scaled to the output resolution. Revives the layout
/// from the legacy WinForms <c>VideoExportEngine.DrawOverlays</c>.
/// </summary>
public static class VideoOverlayRenderer
{
    private static readonly Typeface Regular = new("Segoe UI, Inter, sans-serif");
    private static readonly Typeface Semibold = new(
        new FontFamily("Segoe UI, Inter, sans-serif"), FontStyle.Normal, FontWeight.SemiBold);

    public static void Draw(DrawingContext ctx, int width, int height, VideoOverlayModel model)
    {
        if (!model.HasAnything)
        {
            return;
        }

        var scale = height / 1080f;
        var margin = 20 * scale;

        var overlayBottom = height - margin;

        if (model.ShowProgressBar)
        {
            var barH = Math.Max(3f, 4 * scale);
            var barY = height - margin - barH;
            DrawProgressBar(ctx, width, margin, barH, barY, scale, model);
            overlayBottom = barY - (10 * scale);
        }

        if (model.ShowAlbumArt && model.Cover is { } cover)
        {
            overlayBottom = DrawCover(ctx, width, height, cover, overlayBottom, margin, scale);
        }

        if (model.HasAnyText)
        {
            DrawTextCard(ctx, overlayBottom, margin, scale, model);
        }
    }

    private static void DrawProgressBar(
        DrawingContext ctx, int width, float margin, float barH, float barY, float scale, VideoOverlayModel model)
    {
        var barX = margin;
        var barW = width - (margin * 2);

        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), null,
            new Rect(barX, barY, barW, barH));

        var progress = model.TotalSeconds > 0
            ? Math.Clamp(model.ElapsedSeconds / model.TotalSeconds, 0, 1)
            : 0;
        var fillW = barW * (float)progress;
        if (fillW > 1)
        {
            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromRgb(0x6D, 0x5E, 0xF0), 0),
                    new GradientStop(Color.FromRgb(0x3B, 0xC9, 0xDB), 1),
                },
            };
            ctx.DrawRectangle(fill, null, new Rect(barX, barY, fillW, barH));
        }

        var timeSize = Math.Max(7f, 11 * scale);
        var left = FormatTime(model.ElapsedSeconds);
        var right = FormatTime(model.TotalSeconds);
        var brush = new SolidColorBrush(Color.FromArgb(190, 220, 220, 220));
        var leftText = Format(left, Regular, timeSize, brush);
        var rightText = Format(right, Regular, timeSize, brush);
        var textY = barY - (float)rightText.Height - (4 * scale);
        ctx.DrawText(leftText, new Point(barX, textY));
        ctx.DrawText(rightText, new Point(barX + barW - rightText.Width, textY));
    }

    private static float DrawCover(
        DrawingContext ctx, int width, int height, Bitmap cover, float overlayBottom, float margin, float scale)
    {
        var size = Math.Min(width, height) * 0.14f;
        var x = width - margin - size;
        var y = overlayBottom - size;
        var rect = new Rect(x, y, size, size);
        var radius = 8 * scale;

        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), null,
            new RoundedRect(new Rect(x + (3 * scale), y + (3 * scale), size, size), radius));

        using (ctx.PushClip(new RoundedRect(rect, radius)))
        {
            ctx.DrawImage(cover, new Rect(cover.Size), rect);
        }

        return y - (8 * scale);
    }

    private static void DrawTextCard(
        DrawingContext ctx, float overlayBottom, float margin, float scale, VideoOverlayModel model)
    {
        var titleSize = Math.Max(10f, 22 * scale);
        var subSize = Math.Max(8f, 15 * scale);
        var albumSize = Math.Max(8f, 13 * scale);
        var pad = 12 * scale;
        var gap = 4 * scale;
        var radius = 10 * scale;

        var titleBrush = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255));
        var subBrush = new SolidColorBrush(Color.FromArgb(185, 200, 200, 200));

        var lines = new List<FormattedText>();
        if (model.ShowTitle && !string.IsNullOrWhiteSpace(model.Title))
            lines.Add(Format(model.Title, Semibold, titleSize, titleBrush));
        if (model.ShowArtist && !string.IsNullOrWhiteSpace(model.Artist))
            lines.Add(Format(model.Artist, Regular, subSize, subBrush));
        if (model.ShowAlbum && !string.IsNullOrWhiteSpace(model.Album))
            lines.Add(Format(model.Album, Regular, albumSize, subBrush));

        if (lines.Count == 0)
        {
            return;
        }

        var textW = lines.Max(l => l.Width);
        var textH = lines.Sum(l => l.Height) + (gap * (lines.Count - 1));
        var panelW = textW + (pad * 2);
        var panelH = textH + (pad * 2);
        var panelY = overlayBottom - panelH;

        ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(165, 0, 0, 0)), null,
            new RoundedRect(new Rect(margin, panelY, panelW, panelH), radius));

        var cursorY = panelY + pad;
        foreach (var line in lines)
        {
            ctx.DrawText(line, new Point(margin + pad, cursorY));
            cursorY += (float)line.Height + gap;
        }
    }

    private static FormattedText Format(string text, Typeface typeface, double size, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush);

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
