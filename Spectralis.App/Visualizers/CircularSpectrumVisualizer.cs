using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class CircularSpectrumVisualizer : SkiaVisualizerBase
    {
        public override string Id => "circular-spectrum";
        public override string DisplayName => "Circular Spectrum";
        public override string Category => "Spectrum";

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(8, 8, 16));
            if (Spectrum.Length == 0) return;

            float cx = (float)(width / 2);
            float cy = (float)(height / 2);
            float radius = Math.Min(cx, cy) * 0.35f;
            float maxBarLen = Math.Min(cx, cy) * 0.55f;

            float angleStep = MathF.PI * 2f / Spectrum.Length;

            using var paint = new SKPaint { IsAntialias = true, StrokeWidth = 2.5f, Style = SKPaintStyle.Stroke };
            using var corePaint = new SKPaint { IsAntialias = true, Color = new SKColor(80, 60, 180, 60) };

            canvas.DrawCircle(cx, cy, radius, corePaint);

            for (int i = 0; i < Spectrum.Length; i++)
            {
                float angle = i * angleStep - MathF.PI / 2f;
                float barLen = Spectrum[i] * maxBarLen;

                float x0 = cx + MathF.Cos(angle) * radius;
                float y0 = cy + MathF.Sin(angle) * radius;
                float x1 = cx + MathF.Cos(angle) * (radius + barLen);
                float y1 = cy + MathF.Sin(angle) * (radius + barLen);

                float hue = (i / (float)Spectrum.Length) * 360f;
                paint.Color = HsvToColor(hue, 0.8f, 0.9f);
                canvas.DrawLine(x0, y0, x1, y1, paint);
            }
        }
    }
}
