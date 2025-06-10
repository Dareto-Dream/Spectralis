using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis.Visualizers
{
    public class NeonRingsVisualizer : VisualizerBase
    {
        public override string Name => "Neon Rings";

        private float _rotation;
        private readonly float[] _ringRadii = { 0.2f, 0.32f, 0.44f, 0.56f, 0.68f };
        private readonly float[] _smoothedBands = new float[5];
        private const float Decay = 0.1f;

        public override void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillRectangle(Brushes.Black, bounds);

            float cx = bounds.Left + bounds.Width / 2f;
            float cy = bounds.Top + bounds.Height / 2f;
            float maxR = Math.Min(bounds.Width, bounds.Height) / 2f;

            if (spectrum != null)
            {
                int bandsPerRing = Math.Max(1, spectrum.Length / 5);
                for (int ring = 0; ring < 5; ring++)
                {
                    float sum = 0;
                    int from = ring * bandsPerRing;
                    int to = Math.Min((ring + 1) * bandsPerRing, spectrum.Length);
                    for (int b = from; b < to; b++) sum += spectrum[b];
                    float val = Math.Min(1f, sum / bandsPerRing * 5f);
                    _smoothedBands[ring] = _smoothedBands[ring] * (1f - Decay) + val * Decay;
                }
            }

            _rotation += 0.8f;
            if (_rotation > 360f) _rotation -= 360f;

            for (int ring = 0; ring < _ringRadii.Length; ring++)
            {
                float r = _ringRadii[ring] * maxR;
                float pulse = 1f + _smoothedBands[ring] * 0.3f;
                float pr = r * pulse;

                float hue = (ring * 65f + _rotation) % 360f;
                var baseColor = HsvToRgb(hue, 1f, 1f);
                int glow = (int)(60 + _smoothedBands[ring] * 120);

                using var glowPen = new Pen(Color.FromArgb(glow, baseColor), 8f);
                g.DrawEllipse(glowPen, cx - pr, cy - pr, pr * 2, pr * 2);

                using var pen = new Pen(baseColor, 2f);
                g.DrawEllipse(pen, cx - pr, cy - pr, pr * 2, pr * 2);
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
            return Color.FromArgb((int)((r + m) * 255), (int)((gr + m) * 255), (int)((b + m) * 255));
        }

        public override void Reset()
        {
            _rotation = 0;
            Array.Clear(_smoothedBands, 0, _smoothedBands.Length);
        }
    }
}
