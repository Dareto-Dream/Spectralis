using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class OscilloscopeVisualizer : SkiaVisualizerBase
    {
        public override string Id => "oscilloscope";
        public override string DisplayName => "Oscilloscope";
        public override string Category => "Waveform";

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(5, 20, 5));
            if (Waveform.Length < 2) return;

            float w = (float)width;
            float h = (float)height;
            float half = h / 2f;
            float step = w / (Waveform.Length - 1);

            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 4f,
                Color = new SKColor(0, 255, 0, 30),
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4f)
            };
            using var linePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                Color = new SKColor(0, 255, 60),
                IsAntialias = true
            };

            using var path = new SKPath();
            path.MoveTo(0, half - Waveform[0] * half * 0.85f);
            for (int i = 1; i < Waveform.Length; i++)
                path.LineTo(i * step, half - Waveform[i] * half * 0.85f);

            canvas.DrawPath(path, glowPaint);
            canvas.DrawPath(path, linePaint);
        }
    }
}
