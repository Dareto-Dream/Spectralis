using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class MirrorVisualizer : SkiaVisualizerBase
    {
        public override string Id => "mirror";
        public override string DisplayName => "Mirror Spectrum";
        public override string Category => "Spectrum";

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(14, 14, 20));
            if (Spectrum.Length == 0) return;

            float w = (float)width;
            float h = (float)height;
            float half = h / 2f;
            float barW = w / Spectrum.Length;

            using var paint = new SKPaint { IsAntialias = false };

            for (int i = 0; i < Spectrum.Length; i++)
            {
                float barH = Spectrum[i] * half * 0.95f;
                float x = i * barW;

                float hue = 200f + (i / (float)Spectrum.Length) * 120f;
                paint.Color = HsvToColor(hue % 360f, 0.85f, 0.9f);

                canvas.DrawRect(x, half - barH, Math.Max(1, barW - 1), barH, paint);
                canvas.DrawRect(x, half, Math.Max(1, barW - 1), barH, paint);
            }
        }
    }
}
