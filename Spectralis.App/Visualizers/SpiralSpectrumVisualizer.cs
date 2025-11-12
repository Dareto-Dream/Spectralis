using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class SpiralSpectrumVisualizer : SkiaVisualizerBase
    {
        public override string Id => "spiral";
        public override string DisplayName => "Spiral Spectrum";
        public override string Category => "Spectrum";

        private float _rotation;

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(6, 6, 14));
            if (Spectrum.Length == 0) return;

            _rotation += 0.005f;
            float cx = (float)(width / 2);
            float cy = (float)(height / 2);
            float maxR = Math.Min(cx, cy) * 0.9f;

            using var path = new SKPath();
            bool first = true;

            for (int i = 0; i < Spectrum.Length; i++)
            {
                float t = i / (float)Spectrum.Length;
                float angle = t * MathF.PI * 4f + _rotation;
                float r = t * maxR * 0.7f + Spectrum[i] * maxR * 0.3f;

                float x = cx + MathF.Cos(angle) * r;
                float y = cy + MathF.Sin(angle) * r;

                if (first) { path.MoveTo(x, y); first = false; }
                else path.LineTo(x, y);
            }

            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint((float)width, (float)height),
                    new[] { new SKColor(200, 80, 255), new SKColor(0, 200, 255), new SKColor(255, 100, 80) },
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawPath(path, paint);
        }
    }
}
