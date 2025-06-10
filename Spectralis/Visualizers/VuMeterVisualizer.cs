using System;
using System.Drawing;

namespace Spectralis.Visualizers
{
    public class VuMeterVisualizer : VisualizerBase
    {
        public override string Name => "VU Meter";

        private float _levelL;
        private float _levelR;
        private float _peakL;
        private float _peakR;
        private float _peakHoldL;
        private float _peakHoldR;
        private int _peakHoldFrames;
        private const int PeakHoldDuration = 60;
        private const float Decay = 0.08f;
        private const float PeakDecay = 0.005f;

        public override void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform)
        {
            g.FillRectangle(Brushes.Black, bounds);

            if (waveform == null || waveform.Length == 0) return;

            float rmsL = 0, rmsR = 0;
            int half = waveform.Length / 2;
            for (int i = 0; i < waveform.Length; i += 2)
            {
                if (i < waveform.Length) rmsL += waveform[i] * waveform[i];
                if (i + 1 < waveform.Length) rmsR += waveform[i + 1] * waveform[i + 1];
            }
            rmsL = (float)Math.Sqrt(rmsL / (waveform.Length / 2f));
            rmsR = (float)Math.Sqrt(rmsR / (waveform.Length / 2f));

            _levelL = Math.Max(rmsL, _levelL * (1f - Decay));
            _levelR = Math.Max(rmsR, _levelR * (1f - Decay));

            if (_levelL > _peakL || _levelR > _peakR) _peakHoldFrames = PeakHoldDuration;
            _peakL = Math.Max(_levelL, _peakHoldFrames > 0 ? _peakL : _peakL * (1f - PeakDecay));
            _peakR = Math.Max(_levelR, _peakHoldFrames > 0 ? _peakR : _peakR * (1f - PeakDecay));
            if (_peakHoldFrames > 0) _peakHoldFrames--;

            int meterW = (bounds.Width - 20) / 2;
            DrawMeter(g, new Rectangle(bounds.Left + 5, bounds.Top + 10, meterW, bounds.Height - 20), _levelL, _peakL, "L");
            DrawMeter(g, new Rectangle(bounds.Left + meterW + 15, bounds.Top + 10, meterW, bounds.Height - 20), _levelR, _peakR, "R");
        }

        private void DrawMeter(Graphics g, Rectangle r, float level, float peak, string label)
        {
            int segments = 20;
            int segH = (r.Height - segments) / segments;

            for (int i = 0; i < segments; i++)
            {
                float threshold = (float)(segments - i - 1) / segments;
                bool active = level >= threshold;
                Color c = i < 2
                    ? (active ? Color.Red : Color.FromArgb(60, 0, 0))
                    : i < 4
                        ? (active ? Color.Yellow : Color.FromArgb(60, 60, 0))
                        : (active ? Color.Lime : Color.FromArgb(0, 40, 0));

                int y = r.Top + i * (segH + 1);
                g.FillRectangle(new SolidBrush(c), r.Left, y, r.Width, segH);
            }

            int peakSeg = (int)((1f - peak) * segments);
            peakSeg = Math.Max(0, Math.Min(segments - 1, peakSeg));
            int peakY = r.Top + peakSeg * (segH + 1);
            g.DrawLine(Pens.White, r.Left, peakY, r.Right, peakY);

            g.DrawString(label, SystemFonts.SmallCaptionFont, Brushes.Gray,
                r.Left + r.Width / 2f - 4, r.Bottom + 2);
        }

        public override void Reset()
        {
            _levelL = _levelR = _peakL = _peakR = 0;
            _peakHoldFrames = 0;
        }
    }
}
