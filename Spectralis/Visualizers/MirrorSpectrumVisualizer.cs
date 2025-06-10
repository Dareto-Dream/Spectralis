using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis.Visualizers
{
    public class MirrorSpectrumVisualizer : VisualizerBase
    {
        public override string Name => "Mirror Spectrum";

        private readonly float[] _smoothed;
        private const float Decay = 0.15f;

        public MirrorSpectrumVisualizer(int bands = 64)
        {
            _smoothed = new float[bands];
        }

        public override void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform)
        {
            g.SmoothingMode = SmoothingMode.None;
            g.FillRectangle(Brushes.Black, bounds);

            if (spectrum == null || spectrum.Length == 0) return;

            int bands = Math.Min(spectrum.Length, _smoothed.Length);
            int midY = bounds.Top + bounds.Height / 2;
            int barW = Math.Max(2, bounds.Width / bands);

            for (int i = 0; i < bands; i++)
            {
                float val = Math.Min(1f, spectrum[i] * 4f);
                _smoothed[i] = _smoothed[i] * (1f - Decay) + val * Decay;

                int halfH = (int)(_smoothed[i] * bounds.Height / 2f);
                int x = bounds.Left + i * barW;

                float hue = 200f + _smoothed[i] * 60f;
                var topColor = HsvToRgb(hue, 1f, 0.9f + _smoothed[i] * 0.1f);
                var botColor = HsvToRgb(hue + 20f, 0.8f, 0.6f);

                if (halfH > 0)
                {
                    using var brushTop = new LinearGradientBrush(
                        new Point(x, midY - halfH), new Point(x, midY),
                        topColor, botColor);
                    g.FillRectangle(brushTop, x, midY - halfH, barW - 1, halfH);

                    using var brushBot = new LinearGradientBrush(
                        new Point(x, midY), new Point(x, midY + halfH),
                        botColor, topColor);
                    g.FillRectangle(brushBot, x, midY, barW - 1, halfH);
                }
            }

            using var midPen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f);
            g.DrawLine(midPen, bounds.Left, midY, bounds.Right, midY);
        }

        private static Color HsvToRgb(float h, float s, float v)
        {
            h = h % 360f;
            float c = v * s;
            float x = c * (1 - Math.Abs((h / 60f) % 2 - 1));
            float m = v - c;
            float r, gr, b;
            if (h < 60) { r = c; gr = x; b = 0; }
            else if (h < 120) { r = x; gr = c; b = 0; }
            else if (h < 180) { r = 0; gr = c; b = x; }
            else if (h < 240) { r = 0; gr = x; b = c; }
            else if (h < 300) { r = x; gr = 0; b = c; }
            else { r = c; gr = 0; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((gr + m) * 255), (int)((b + m) * 255));
        }

        public override void Reset()
        {
            Array.Clear(_smoothed, 0, _smoothed.Length);
        }
    }
}
