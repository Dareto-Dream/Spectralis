using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class HeatmapVisualizer : SkiaVisualizerBase
    {
        public override string Id => "heatmap";
        public override string DisplayName => "Spectrum Heatmap";
        public override string Category => "Spectrum";

        private const int HistoryLines = 120;
        private readonly float[][] _history = new float[HistoryLines][];
        private int _historyIndex;

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(5, 5, 10));
            if (Spectrum.Length == 0) return;

            _history[_historyIndex] = (float[])Spectrum.Clone();
            _historyIndex = (_historyIndex + 1) % HistoryLines;

            float lineH = (float)height / HistoryLines;
            float barW = (float)width / Spectrum.Length;

            using var paint = new SKPaint { IsAntialias = false };

            for (int row = 0; row < HistoryLines; row++)
            {
                int histIdx = (_historyIndex + row) % HistoryLines;
                if (_history[histIdx] == null) continue;

                float y = (float)height - (row + 1) * lineH;

                for (int col = 0; col < _history[histIdx].Length; col++)
                {
                    float val = _history[histIdx][col];
                    float age = row / (float)HistoryLines;
                    byte alpha = (byte)(255 * (1 - age * 0.7f));

                    float hue = 240f - val * 240f;
                    paint.Color = HsvToColor(Math.Max(0, hue), 1f, Math.Min(1f, val * 2f), alpha);
                    canvas.DrawRect(col * barW, y, barW, lineH + 1, paint);
                }
            }
        }
    }
}
