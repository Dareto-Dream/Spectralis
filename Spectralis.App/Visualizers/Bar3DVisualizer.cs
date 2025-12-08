using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class Bar3DVisualizer : SkiaVisualizerBase
    {
        public override string Id => "bars-3d";
        public override string DisplayName => "3D Bars";
        public override string Category => "Spectrum";

        private SKSurface? _offscreen;
        private int _lastW, _lastH;

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            int w = (int)width;
            int h = (int)height;

            if (_offscreen == null || w != _lastW || h != _lastH)
            {
                _offscreen?.Dispose();
                _offscreen = SKSurface.Create(new SKImageInfo(w, h));
                _lastW = w;
                _lastH = h;
            }

            var offCanvas = _offscreen.Canvas;
            offCanvas.Clear(new SKColor(10, 10, 18));

            if (Spectrum.Length == 0)
            {
                using var img = _offscreen.Snapshot();
                canvas.DrawImage(img, 0, 0);
                return;
            }

            int bars = Math.Min(32, Spectrum.Length);
            float barW = w / (float)bars * 0.7f;
            float gap = w / (float)bars * 0.3f;
            float depth = 14f;
            float depthX = depth * 0.6f;
            float depthY = depth * 0.5f;

            using var facePaint = new SKPaint { IsAntialias = true };
            using var topPaint = new SKPaint { IsAntialias = true };
            using var sidePaint = new SKPaint { IsAntialias = true };

            for (int i = bars - 1; i >= 0; i--)
            {
                float barH = Spectrum[i * Spectrum.Length / bars] * (h * 0.85f);
                if (barH < 2f) continue;

                float x = i * (w / (float)bars) + gap / 2f;
                float y = h - barH;

                float hue = 200f + (i / (float)bars) * 140f;
                facePaint.Color = HsvToColor(hue % 360f, 0.75f, 0.85f);
                sidePaint.Color = HsvToColor(hue % 360f, 0.6f, 0.55f);
                topPaint.Color = HsvToColor(hue % 360f, 0.5f, 0.98f);

                offCanvas.DrawRect(x, y, barW, barH, facePaint);

                var topPath = new SKPath();
                topPath.MoveTo(x, y);
                topPath.LineTo(x + depthX, y - depthY);
                topPath.LineTo(x + barW + depthX, y - depthY);
                topPath.LineTo(x + barW, y);
                topPath.Close();
                offCanvas.DrawPath(topPath, topPaint);

                var sidePath = new SKPath();
                sidePath.MoveTo(x + barW, y);
                sidePath.LineTo(x + barW + depthX, y - depthY);
                sidePath.LineTo(x + barW + depthX, h - depthY);
                sidePath.LineTo(x + barW, h);
                sidePath.Close();
                offCanvas.DrawPath(sidePath, sidePaint);

                topPath.Dispose();
                sidePath.Dispose();
            }

            using var image = _offscreen.Snapshot();
            canvas.DrawImage(image, 0, 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { }
        }
    }
}
