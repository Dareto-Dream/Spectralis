using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis.Visualizers
{
    public class BeatPulseVisualizer : VisualizerBase
    {
        public override string Name => "Beat Pulse";

        private float _pulseRadius;
        private float _pulseAlpha;
        private float _lastBass;
        private readonly float[] _rings = new float[6];
        private readonly float[] _ringAlphas = new float[6];
        private int _ringHead;

        public override void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillRectangle(Brushes.Black, bounds);

            float cx = bounds.Left + bounds.Width / 2f;
            float cy = bounds.Top + bounds.Height / 2f;
            float maxR = Math.Min(bounds.Width, bounds.Height) / 2f;

            float bass = 0;
            if (spectrum != null)
            {
                for (int i = 0; i < Math.Min(8, spectrum.Length); i++)
                    bass += spectrum[i];
                bass /= 8f;
                bass = Math.Min(1f, bass * 5f);
            }

            bool beat = bass > 0.6f && bass > _lastBass + 0.15f;
            _lastBass = bass;

            if (beat)
            {
                _rings[_ringHead] = 0f;
                _ringAlphas[_ringHead] = 1f;
                _ringHead = (_ringHead + 1) % _rings.Length;
            }

            for (int i = 0; i < _rings.Length; i++)
            {
                if (_ringAlphas[i] <= 0) continue;
                _rings[i] += 3f;
                _ringAlphas[i] = Math.Max(0, _ringAlphas[i] - 0.02f);

                float r = _rings[i] * maxR / 100f;
                int alpha = (int)(_ringAlphas[i] * 200);
                using var pen = new Pen(Color.FromArgb(alpha, 80, 180, 255), 2f);
                g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
            }

            float coreR = 20f + bass * 40f;
            int coreAlpha = (int)(120 + bass * 135);
            using var coreBrush = new SolidBrush(Color.FromArgb(coreAlpha, 100, 200, 255));
            g.FillEllipse(coreBrush, cx - coreR, cy - coreR, coreR * 2, coreR * 2);
        }

        public override void Reset()
        {
            Array.Clear(_rings, 0, _rings.Length);
            Array.Clear(_ringAlphas, 0, _ringAlphas.Length);
            _ringHead = 0;
            _lastBass = 0;
        }
    }
}
