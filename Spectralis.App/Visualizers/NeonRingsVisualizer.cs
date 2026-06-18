using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class NeonRingsVisualizer : SkiaVisualizerBase
    {
        public override string Id => "neon-rings";
        public override string DisplayName => "Neon Rings";
        public override string Category => "Spectrum";

        private float _phase;
        private readonly SKPaint _paint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _glowPaint = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f)
        };

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(4, 0, 12));
            if (Spectrum.Length == 0) return;

            _phase += 0.01f;
            float cx = (float)(width / 2);
            float cy = (float)(height / 2);
            float maxR = Math.Min(cx, cy) * 0.9f;

            int rings = Math.Min(12, Spectrum.Length / 4);
            float ringStep = maxR / rings;

            for (int r = 0; r < rings; r++)
            {
                int band = r * Spectrum.Length / rings;
                float energy = Spectrum[band];
                float radius = (r + 1) * ringStep + energy * ringStep * 0.6f;
                float hue = (_phase * 60f + r * 30f) % 360f;
                byte alpha = (byte)(100 + energy * 155);

                _paint.Color = HsvToColor(hue, 1f, 0.95f, alpha);
                _paint.StrokeWidth = 1.5f + energy * 3f;
                _glowPaint.Color = HsvToColor(hue, 0.8f, 0.8f, (byte)(alpha / 3));
                _glowPaint.StrokeWidth = _paint.StrokeWidth + 4f;

                canvas.DrawCircle(cx, cy, radius, _glowPaint);
                canvas.DrawCircle(cx, cy, radius, _paint);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _paint.Dispose();
                _glowPaint.MaskFilter?.Dispose();
                _glowPaint.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
