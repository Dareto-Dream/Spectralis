using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class WaterfallVisualizer : SkiaVisualizerBase
    {
        public override string Id => "waterfall";
        public override string DisplayName => "Waterfall";
        public override string Category => "Spectrum";

        private const int MaxLines = 200;
        private readonly float[][] _lines = new float[MaxLines][];
        private int _head;

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(0, 0, 8));
            if (Spectrum.Length == 0) return;

            _lines[_head] = (float[])Spectrum.Clone();
            _head = (_head + 1) % MaxLines;

            float lineH = (float)height / MaxLines;
            float barW = (float)width / Spectrum.Length;

            using var paint = new SKPaint { IsAntialias = false };

            for (int row = 0; row < MaxLines; row++)
            {
                int idx = (_head + row) % MaxLines;
                if (_lines[idx] == null) continue;

                float y = (float)height - (row + 1) * lineH;

                for (int b = 0; b < _lines[idx].Length; b++)
                {
                    float v = _lines[idx][b];
                    float hue = 240f - v * 240f;
                    byte a = (byte)(180 + v * 75);
                    paint.Color = HsvToColor(Math.Max(0f, hue), 1f, Math.Min(1f, v * 2.5f), a);
                    canvas.DrawRect(b * barW, y, barW + 0.5f, lineH + 0.5f, paint);
                }
            }
        }
    }
}
