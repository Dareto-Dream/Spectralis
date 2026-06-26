using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

public sealed class SpinningDiskRenderer : VisualizerRendererBase
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);
        DrawGrid(canvas, bounds, scene);
        DrawSpinningDisk(canvas, bounds, scene);
    }

    private static void DrawSpinningDisk(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var size = Math.Min(bounds.Width, bounds.Height) - 40;
        if (size <= 0)
            return;

        var cx = bounds.Left + (bounds.Width / 2);
        var cy = bounds.Top + (bounds.Height / 2);
        var diskRect = new VizRect(cx - (size / 2), cy - (size / 2), size, size);

        canvas.PushClipEllipse(diskRect);

        if (scene.AlbumArt is not null)
        {
            canvas.PushRotation(scene.DiskAngle, new Vector2(cx, cy));
            canvas.DrawImage(scene.AlbumArt, new VizRect(cx - (size / 2f), cy - (size / 2f), size, size));
            canvas.Restore();

            canvas.FillEllipse(diskRect, new VizColor(48, 0, 0, 0));
        }
        else
        {
            canvas.FillEllipse(diskRect, scene.Theme.DiskFillColor);
        }

        var grooveAlpha = scene.AlbumArt is not null ? 20 : 45;
        var grooveColor = scene.Theme.DiskGrooveColor.WithAlpha(grooveAlpha);
        for (var radius = size / 5; radius < (size / 2) - 4; radius += 10)
        {
            canvas.DrawEllipse(new VizRect(cx - radius, cy - radius, radius * 2, radius * 2), grooveColor, 1f);
        }

        canvas.Restore();

        var hub = Math.Max(22, size / 6);
        canvas.FillEllipse(new VizRect(cx - (hub / 2), cy - (hub / 2), hub, hub), scene.Theme.HubColor);

        var dot = Math.Max(6, hub / 3);
        canvas.FillEllipse(new VizRect(cx - (dot / 2), cy - (dot / 2), dot, dot), scene.Theme.HubDotColor);

        canvas.DrawEllipse(diskRect, scene.Theme.RingColor.WithAlpha(72), 2f);

        if (!scene.IsActive)
        {
            canvas.DrawText(
                "paused",
                new VizRect(bounds.X, cy - (hub / 2f) - 26, bounds.Width, 20),
                scene.Theme.IdleLabelColor,
                12,
                VizTextAlign.Center);
        }
    }
}
