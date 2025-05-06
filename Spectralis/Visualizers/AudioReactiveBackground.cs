using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis.Visualizers
{
    public class AudioReactiveBackground : VisualizerBase
    {
        public override string Name => "Audio Reactive BG";

        private float _hue;
        private float _lastBass;
        private float _energy;
        private readonly float[] _smoothed = new float[3];

        public override void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (spectrum != null && spectrum.Length >= 32)
            {
                float bass = 0, mid = 0, treble = 0;
                for (int i = 0; i < 8; i++) bass += spectrum[i];
                for (int i = 8; i < 24; i++) mid += spectrum[i];
                for (int i = 24; i < 32; i++) treble += spectrum[i];
                bass /= 8f; mid /= 16f; treble /= 8f;

                _smoothed[0] = _smoothed[0] * 0.85f + bass * 0.15f;
                _smoothed[1] = _smoothed[1] * 0.85f + mid * 0.15f;
                _smoothed[2] = _smoothed[2] * 0.85f + treble * 0.15f;
                _energy = (_smoothed[0] + _smoothed[1] + _smoothed[2]) / 3f;

                if (_smoothed[0] > _lastBass + 0.05f)
                    _hue = (_hue + 15f) % 360f;
                _lastBass = _smoothed[0];
            }

            float hue2 = (_hue + 60f) % 360f;
            var c1 = HsvToRgb(_hue, 0.8f, Math.Min(0.4f + _energy * 0.5f, 1f));
            var c2 = HsvToRgb(hue2, 0.9f, Math.Min(0.2f + _smoothed[1] * 0.4f, 0.8f));

            using var brush = new LinearGradientBrush(bounds, c1, c2, 45f);
            g.FillRectangle(brush, bounds);

            if (waveform != null && waveform.Length > 1)
            {
                float cx = bounds.Left + bounds.Width / 2f;
                float cy = bounds.Top + bounds.Height / 2f;
                float amp = bounds.Height * 0.25f * _energy;
                int pts = Math.Min(waveform.Length, bounds.Width);
                var points = new PointF[pts];
                for (int i = 0; i < pts; i++)
                {
                    float x = bounds.Left + (float)i / pts * bounds.Width;
                    float y = cy + waveform[i * waveform.Length / pts] * amp;
                    points[i] = new PointF(x, y);
                }
                using var wavePen = new Pen(Color.FromArgb(80, 255, 255, 255), 1.5f);
                g.DrawLines(wavePen, points);
            }
        }

        private static Color HsvToRgb(float h, float s, float v)
        {
            h = h % 360f;
            float c = v * s, x = c * (1 - Math.Abs((h / 60f) % 2 - 1)), m = v - c;
            float r, gr, b;
            if (h < 60) { r = c; gr = x; b = 0; }
            else if (h < 120) { r = x; gr = c; b = 0; }
            else if (h < 180) { r = 0; gr = c; b = x; }
            else if (h < 240) { r = 0; gr = x; b = c; }
            else if (h < 300) { r = x; gr = 0; b = c; }
            else { r = c; gr = 0; b = x; }
            return Color.FromArgb(Math.Max(0, (int)((r + m) * 255)), Math.Max(0, (int)((gr + m) * 255)), Math.Max(0, (int)((b + m) * 255)));
        }

        public override void Reset()
        {
            _hue = 0; _lastBass = 0; _energy = 0;
            Array.Clear(_smoothed, 0, _smoothed.Length);
        }
    }
}
