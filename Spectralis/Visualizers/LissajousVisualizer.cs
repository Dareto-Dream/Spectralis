using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis.Visualizers
{
    public class LissajousVisualizer : VisualizerBase
    {
        public override string Name => "Lissajous";

        private const int TrailLength = 512;
        private readonly PointF[] _trail = new PointF[TrailLength];
        private int _trailHead;
        private int _trailCount;

        public override void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillRectangle(new SolidBrush(Color.FromArgb(10, 10, 15)), bounds);

            if (waveform == null || waveform.Length < 2) return;

            float cx = bounds.Left + bounds.Width / 2f;
            float cy = bounds.Top + bounds.Height / 2f;
            float scaleX = bounds.Width * 0.45f;
            float scaleY = bounds.Height * 0.45f;

            int step = Math.Max(1, waveform.Length / TrailLength);
            for (int i = 0; i < waveform.Length - 1 && _trailCount < TrailLength; i += step)
            {
                float l = waveform[i];
                float r = waveform[Math.Min(i + 1, waveform.Length - 1)];
                _trail[_trailHead] = new PointF(cx + l * scaleX, cy - r * scaleY);
                _trailHead = (_trailHead + 1) % TrailLength;
                _trailCount = Math.Min(_trailCount + 1, TrailLength);
            }

            if (_trailCount < 2) return;

            for (int i = 0; i < _trailCount - 1; i++)
            {
                int idx = (_trailHead - _trailCount + i + TrailLength) % TrailLength;
                int next = (_trailHead - _trailCount + i + 1 + TrailLength) % TrailLength;

                float t = (float)i / _trailCount;
                int alpha = (int)(t * 200);
                var c = Color.FromArgb(alpha, 0, (int)(150 + t * 105), (int)(200 + t * 55));

                using var pen = new Pen(c, 1.5f);
                g.DrawLine(pen, _trail[idx], _trail[next]);
            }
        }

        public override void Reset()
        {
            _trailHead = 0;
            _trailCount = 0;
        }
    }
}
