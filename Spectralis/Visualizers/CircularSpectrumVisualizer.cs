using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis.Visualizers
{
    public class CircularSpectrumVisualizer : VisualizerBase
    {
        public override string Name => "Circular Spectrum";

        private readonly float[] _smoothed;
        private const float Decay = 0.1f;

        public CircularSpectrumVisualizer(int bands = 128)
        {
            _smoothed = new float[bands];
        }

        public override void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillRectangle(Brushes.Black, bounds);

            float cx = bounds.Left + bounds.Width / 2f;
            float cy = bounds.Top + bounds.Height / 2f;
            float radius = Math.Min(bounds.Width, bounds.Height) * 0.3f;

            if (spectrum != null)
            {
                int bands = Math.Min(spectrum.Length, _smoothed.Length);
                for (int i = 0; i < bands; i++)
                {
                    float val = Math.Min(1f, spectrum[i] * 5f);
                    _smoothed[i] = _smoothed[i] * (1f - Decay) + val * Decay;

                    float angle = (float)(2 * Math.PI * i / bands) - (float)(Math.PI / 2);
                    float barLen = _smoothed[i] * radius * 0.8f;

                    float x1 = cx + (float)Math.Cos(angle) * radius;
                    float y1 = cy + (float)Math.Sin(angle) * radius;
                    float x2 = cx + (float)Math.Cos(angle) * (radius + barLen);
                    float y2 = cy + (float)Math.Sin(angle) * (radius + barLen);

                    int alpha = (int)(80 + _smoothed[i] * 175);
                    using var pen = new Pen(Color.FromArgb(alpha, 100, 200, 255), 2f);
                    g.DrawLine(pen, x1, y1, x2, y2);
                }
            }

            using var circlePen = new Pen(Color.FromArgb(60, 100, 180, 255), 1f);
            g.DrawEllipse(circlePen, cx - radius, cy - radius, radius * 2, radius * 2);
        }

        public override void Reset()
        {
            Array.Clear(_smoothed, 0, _smoothed.Length);
        }
    }
}
