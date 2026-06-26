using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

internal sealed class AlbumCoverVisualizerRenderer : VisualizerRendererBase
{
    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        DrawBackground(graphics, bounds, scene);

        if (scene.AlbumArt is null)
        {
            DrawPlaceholder(graphics, bounds, scene);
            return;
        }

        DrawAmbientCover(graphics, bounds, scene.AlbumArt);
        DrawCover(graphics, bounds, scene.AlbumArt);
    }

    private static void DrawAmbientCover(Graphics graphics, Rectangle bounds, Image albumArt)
    {
        var bleed = Math.Max(bounds.Width, bounds.Height);
        var bleedRect = new Rectangle(
            bounds.Left + (bounds.Width - bleed) / 2,
            bounds.Top + (bounds.Height - bleed) / 2,
            bleed,
            bleed);

        var state = graphics.Save();
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(albumArt, bleedRect);

        using var wash = new SolidBrush(Color.FromArgb(188, 0, 0, 0));
        graphics.FillRectangle(wash, bounds);
        graphics.Restore(state);
    }

    private static void DrawCover(Graphics graphics, Rectangle bounds, Image albumArt)
    {
        var margin = Math.Max(24, Math.Min(bounds.Width, bounds.Height) / 18);
        var maxWidth = Math.Max(1, bounds.Width - (margin * 2));
        var maxHeight = Math.Max(1, bounds.Height - (margin * 2));
        var scale = Math.Min(maxWidth / (float)albumArt.Width, maxHeight / (float)albumArt.Height);
        var width = Math.Max(1, (int)Math.Round(albumArt.Width * scale));
        var height = Math.Max(1, (int)Math.Round(albumArt.Height * scale));
        var coverRect = new Rectangle(
            bounds.Left + (bounds.Width - width) / 2,
            bounds.Top + (bounds.Height - height) / 2,
            width,
            height);

        using var shadowPath = CreateRoundedRectangle(coverRect with { X = coverRect.X + 10, Y = coverRect.Y + 14 }, 8);
        using var shadowBrush = new SolidBrush(Color.FromArgb(92, 0, 0, 0));
        graphics.FillPath(shadowBrush, shadowPath);

        var state = graphics.Save();
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using var clipPath = CreateRoundedRectangle(coverRect, 8);
        graphics.SetClip(clipPath, CombineMode.Intersect);
        graphics.DrawImage(albumArt, coverRect);
        graphics.Restore(state);

        using var borderPath = CreateRoundedRectangle(coverRect, 8);
        using var borderPen = new Pen(Color.FromArgb(64, 255, 255, 255), 1.5f);
        graphics.DrawPath(borderPen, borderPath);
    }
}
