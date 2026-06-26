using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis;

internal sealed class SpinningDiskVisualizerRenderer : VisualizerRendererBase
{
    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        DrawBackground(graphics, bounds, scene);
        DrawGrid(graphics, bounds, scene);
        DrawSpinningDisk(graphics, bounds, scene);
    }

    private static void DrawSpinningDisk(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var size = Math.Min(bounds.Width, bounds.Height) - 40;
        if (size <= 0)
            return;

        var cx = bounds.Left + bounds.Width / 2;
        var cy = bounds.Top + bounds.Height / 2;
        var diskRect = new Rectangle(cx - size / 2, cy - size / 2, size, size);

        using var clipPath = new GraphicsPath();
        clipPath.AddEllipse(diskRect);

        var phase1 = graphics.Save();
        graphics.SetClip(clipPath, CombineMode.Intersect);

        if (scene.AlbumArt is not null)
        {
            var phase2 = graphics.Save();
            graphics.TranslateTransform(cx, cy);
            graphics.RotateTransform(scene.DiskAngle);
            graphics.DrawImage(scene.AlbumArt, -size / 2f, -size / 2f, size, size);
            graphics.Restore(phase2);

            using var tint = new SolidBrush(Color.FromArgb(48, 0, 0, 0));
            graphics.FillEllipse(tint, diskRect);
        }
        else
        {
            using var fill = new SolidBrush(scene.Theme.DiskFillColor);
            graphics.FillEllipse(fill, diskRect);
        }

        var grooveAlpha = scene.AlbumArt is not null ? 20 : 45;
        using var groovePen = new Pen(Color.FromArgb(grooveAlpha, scene.Theme.DiskGrooveColor), 1f);
        for (var radius = size / 5; radius < size / 2 - 4; radius += 10)
            graphics.DrawEllipse(groovePen, cx - radius, cy - radius, radius * 2, radius * 2);

        graphics.Restore(phase1);

        var hub = Math.Max(22, size / 6);
        using var hubBrush = new SolidBrush(scene.Theme.HubColor);
        graphics.FillEllipse(hubBrush, cx - hub / 2, cy - hub / 2, hub, hub);

        var dot = Math.Max(6, hub / 3);
        using var dotBrush = new SolidBrush(scene.Theme.HubDotColor);
        graphics.FillEllipse(dotBrush, cx - dot / 2, cy - dot / 2, dot, dot);

        using var ring = new Pen(Color.FromArgb(72, scene.Theme.RingColor), 2f);
        graphics.DrawEllipse(ring, diskRect);

        if (!scene.IsActive)
        {
            using var textBrush = new SolidBrush(scene.Theme.IdleLabelColor);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Far
            };
            graphics.DrawString("paused", scene.Font, textBrush, new RectangleF(bounds.X, bounds.Y, bounds.Width, cy - hub / 2f - 6), format);
        }
    }
}
