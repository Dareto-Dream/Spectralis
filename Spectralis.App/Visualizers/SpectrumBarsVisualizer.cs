using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class SpectrumBarsVisualizer : SkiaVisualizerBase
    {
        public override string Id => "spectrum-bars";
        public override string DisplayName => "Spectrum Bars";
        public override string Category => "Spectrum";

        private float[] _peaks = Array.Empty<float>();
        private float[] _peakVelocities = Array.Empty<float>();

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(14, 14, 20));
            if (Spectrum.Length == 0) return;

            if (_peaks.Length != Spectrum.Length)
            {
                _peaks = new float[Spectrum.Length];
                _peakVelocities = new float[Spectrum.Length];
            }

            float barW = (float)(width / Spectrum.Length);
            float h = (float)height;

            using var barPaint = new SKPaint { IsAntialias = true };
            using var peakPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 1.5f, IsStroke = true };

            for (int i = 0; i < Spectrum.Length; i++)
            {
                float barH = Math.Max(2f, Spectrum[i] * h);
                float x = i * barW;

                float hue = (i / (float)Spectrum.Length) * 240f;
                barPaint.Color = HsvToColor(hue, 0.9f, 0.85f);
                canvas.DrawRect(x + 1, h - barH, Math.Max(1, barW - 2), barH, barPaint);

                if (Spectrum[i] > _peaks[i])
                {
                    _peaks[i] = Spectrum[i];
                    _peakVelocities[i] = 0f;
                }
                else
                {
                    _peakVelocities[i] += 0.002f;
                    _peaks[i] = Math.Max(0, _peaks[i] - _peakVelocities[i]);
                }

                float peakY = h - _peaks[i] * h;
                canvas.DrawLine(x + 1, peakY, x + barW - 1, peakY, peakPaint);
            }
        }
    }
}
