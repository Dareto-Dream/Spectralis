using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis.Visualizers
{
    public class BarSpectrumVisualizer3D : VisualizerBase
    {
        public override string Name => "3D Bar Spectrum";

        private readonly float[] _smoothed;
        private const float Decay = 0.14f;
        private const float Depth = 14f;

        public BarSpectrumVisualizer3D(int bands = 48)
        {
            _smoothed = new float[bands];
        }

        public override void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillRectangle(new SolidBrush(Color.FromArgb(12, 12, 18)), bounds);

            if (spectrum == null || spectrum.Length == 0) return;

            int bands = Math.Min(spectrum.Length, _smoothed.Length);
            int barW = Math.Max(3, (bounds.Width - 20) / bands);
            int gap = Math.Max(1, barW / 5);

            for (int i = 0; i < bands; i++)
            {
                float val = Math.Min(1f, spectrum[i] * 4.5f);
                _smoothed[i] = _smoothed[i] * (1f - Decay) + val * Decay;

                int bh = (int)(_smoothed[i] * (bounds.Height - 20));
                int x = bounds.Left + 10 + i * (barW + gap);
                int y = bounds.Bottom - 10 - bh;

                float hue = 180f + (float)i / bands * 120f;
                var front = HsvToRgb(hue, 0.9f, 0.85f + _smoothed[i] * 0.15f);
                var side = HsvToRgb(hue, 0.9f, 0.5f);
                var top = HsvToRgb(hue, 0.6f, 0.95f);

                var frontRect = new Rectangle(x, y, barW, bh);
                var sidePoints = new PointF[]
                {
                    new PointF(x + barW, y),
                    new PointF(x + barW + Depth, y - Depth),
                    new PointF(x + barW + Depth, bounds.Bottom - 10 - Depth),
                    new PointF(x + barW, bounds.Bottom - 10)
                };
                var topPoints = new PointF[]
                {
                    new PointF(x, y),
                    new PointF(x + Depth, y - Depth),
                    new PointF(x + barW + Depth, y - Depth),
                    new PointF(x + barW, y)
                };

                if (bh > 0)
                {
                    using var frontBrush = new LinearGradientBrush(new Point(x, y), new Point(x, bounds.Bottom), front, HsvToRgb(hue, 1f, 0.4f));
                    g.FillRectangle(frontBrush, frontRect);
                    g.FillPolygon(new SolidBrush(side), sidePoints);
                    g.FillPolygon(new SolidBrush(top), topPoints);
                }
            }
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
