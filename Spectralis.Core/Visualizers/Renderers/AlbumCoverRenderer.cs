namespace Spectralis.Core.Visualizers.Renderers;

/// <summary>Full-bleed ambient album art with a centered rounded cover.</summary>
public sealed class AlbumCoverRenderer : VisualizerRendererBase
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);

        if (scene.AlbumArt is null)
        {
            DrawPlaceholder(canvas, bounds, scene);
            return;
        }

        DrawAmbientCover(canvas, bounds, scene.AlbumArt);
        DrawCover(canvas, bounds, scene.AlbumArt);
    }

    private static void DrawAmbientCover(IVizCanvas canvas, VizRect bounds, IVizImage albumArt)
    {
        var bleed = Math.Max(bounds.Width, bounds.Height);
        var bleedRect = new VizRect(
            bounds.Left + ((bounds.Width - bleed) / 2),
            bounds.Top + ((bounds.Height - bleed) / 2),
            bleed,
            bleed);

        canvas.PushClipRect(bounds);
        canvas.DrawImage(albumArt, bleedRect);
        canvas.FillRect(bounds, new VizColor(188, 0, 0, 0));
        canvas.Restore();
    }

    private static void DrawCover(IVizCanvas canvas, VizRect bounds, IVizImage albumArt)
    {
        var margin = Math.Max(24, Math.Min(bounds.Width, bounds.Height) / 18);
        var maxWidth = Math.Max(1, bounds.Width - (margin * 2));
        var maxHeight = Math.Max(1, bounds.Height - (margin * 2));
        var scale = Math.Min(maxWidth / albumArt.Width, maxHeight / albumArt.Height);
        var width = Math.Max(1, albumArt.Width * scale);
        var height = Math.Max(1, albumArt.Height * scale);
        var coverRect = new VizRect(
            bounds.Left + ((bounds.Width - width) / 2),
            bounds.Top + ((bounds.Height - height) / 2),
            width,
            height);

        canvas.FillRoundedRect(
            coverRect with { X = coverRect.X + 10, Y = coverRect.Y + 14 },
            8,
            new VizColor(92, 0, 0, 0));

        canvas.PushClipRoundedRect(coverRect, 8);
        canvas.DrawImage(albumArt, coverRect);
        canvas.Restore();

        canvas.DrawRoundedRect(coverRect, 8, new VizColor(64, 255, 255, 255), 1.5f);
    }
}
