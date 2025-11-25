using System;
using SkiaSharp;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Visualizers
{
    public class MosaicVisualizer : SkiaVisualizerBase
    {
        public override string Id => "mosaic";
        public override string DisplayName => "Mosaic";
        public override string Category => "Spectrum";

        private readonly ColorPalette _palette = ColorPalette.Neon;
        private float _timePhase;

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(6, 6, 14));
            if (Spectrum.Length == 0) return;

            _timePhase += 0.02f;

            int cols = 16;
            int rows = 10;
            float cellW = (float)width / cols;
            float cellH = (float)height / rows;

            using var paint = new SKPaint { IsAntialias = false };

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    int band = (col * Spectrum.Length / cols + (int)(_timePhase * 5)) % Spectrum.Length;
                    float energy = Spectrum[band];

                    float t = energy * (1f - row / (float)rows * 0.5f);
                    var color = _palette.Sample(t);
                    byte alpha = (byte)(60 + energy * 195);

                    paint.Color = new SKColor(color.Red, color.Green, color.Blue, alpha);
                    canvas.DrawRoundRect(
                        col * cellW + 2, row * cellH + 2,
                        cellW - 4, cellH - 4, 4, 4, paint);
                }
            }
        }
    }
}
