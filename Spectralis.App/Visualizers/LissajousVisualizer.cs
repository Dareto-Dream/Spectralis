using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class LissajousVisualizer : SkiaVisualizerBase
    {
        public override string Id => "lissajous";
        public override string DisplayName => "Lissajous";
        public override string Category => "Waveform";

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(8, 8, 16));
            if (Waveform.Length < 4) return;

            float cx = (float)(width / 2);
            float cy = (float)(height / 2);
            float scale = Math.Min(cx, cy) * 0.88f;

            int half = Waveform.Length / 2;

            using var path = new SKPath();
            float x0 = cx + Waveform[0] * scale;
            float y0 = cy + Waveform[half] * scale;
            path.MoveTo(x0, y0);

            for (int i = 1; i < half; i++)
            {
                path.LineTo(cx + Waveform[i] * scale, cy + Waveform[half + i] * scale);
            }

            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                IsAntialias = true,
                Shader = SKShader.CreateSweepGradient(
                    new SKPoint(cx, cy),
                    new[] { new SKColor(100, 80, 255), new SKColor(0, 200, 255), new SKColor(100, 80, 255) },
                    null)
            };
            canvas.DrawPath(path, paint);
        }
    }
}
