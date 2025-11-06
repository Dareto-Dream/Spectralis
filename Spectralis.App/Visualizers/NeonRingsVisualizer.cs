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

            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
            using var glowPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6f)
            };

            for (int r = 0; r < rings; r++)
            {
                int band = r * Spectrum.Length / rings;
                float energy = Spectrum[band];
                float radius = (r + 1) * ringStep + energy * ringStep * 0.6f;
                float hue = (_phase * 60f + r * 30f) % 360f;
                byte alpha = (byte)(100 + energy * 155);

                paint.Color = HsvToColor(hue, 1f, 0.95f, alpha);
                paint.StrokeWidth = 1.5f + energy * 3f;
                glowPaint.Color = HsvToColor(hue, 0.8f, 0.8f, (byte)(alpha / 3));
                glowPaint.StrokeWidth = paint.StrokeWidth + 4f;

                canvas.DrawCircle(cx, cy, radius, glowPaint);
                canvas.DrawCircle(cx, cy, radius, paint);
            }
        }
    }
}
