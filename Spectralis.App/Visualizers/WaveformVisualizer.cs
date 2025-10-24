using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class WaveformVisualizer : SkiaVisualizerBase
    {
        public override string Id => "waveform";
        public override string DisplayName => "Waveform";
        public override string Category => "Waveform";

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(14, 14, 20));
            if (Waveform.Length < 2) return;

            float w = (float)width;
            float h = (float)height;
            float half = h / 2f;
            float step = w / (Waveform.Length - 1);

            using var path = new SKPath();
            path.MoveTo(0, half - Waveform[0] * half * 0.9f);
            for (int i = 1; i < Waveform.Length; i++)
                path.LineTo(i * step, half - Waveform[i] * half * 0.9f);

            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f,
                IsAntialias = true,
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(w, 0),
                    new[] { new SKColor(100, 100, 255), new SKColor(0, 200, 255) },
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawPath(path, paint);

            using var midPaint = new SKPaint
            {
                Color = new SKColor(30, 30, 50),
                StrokeWidth = 0.5f,
                Style = SKPaintStyle.Stroke
            };
            canvas.DrawLine(0, half, w, half, midPaint);
        }
    }
}
