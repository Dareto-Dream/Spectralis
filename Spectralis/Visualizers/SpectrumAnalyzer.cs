using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis.Visualizers
{
    public class SpectrumAnalyzer : VisualizerBase
    {
        public override string Name => "Spectrum Analyzer";

        private readonly float[] _peaks;
        private readonly float[] _peakVelocities;
        private const float PeakDecay = 0.015f;
        private const float BarDecay = 0.12f;
        private readonly float[] _smoothed;

        public Color BarColorTop { get; set; } = Color.FromArgb(0, 200, 255);
        public Color BarColorBottom { get; set; } = Color.FromArgb(0, 80, 160);
        public Color PeakColor { get; set; } = Color.FromArgb(255, 80, 80);
        public Color BackgroundColor { get; set; } = Color.FromArgb(15, 15, 20);

        public SpectrumAnalyzer(int bandCount = 64)
        {
            _peaks = new float[bandCount];
            _peakVelocities = new float[bandCount];
            _smoothed = new float[bandCount];
        }

        public override void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillRectangle(new SolidBrush(BackgroundColor), bounds);

            if (spectrum == null || spectrum.Length == 0) return;

            int bands = Math.Min(spectrum.Length, _smoothed.Length);
            int barWidth = Math.Max(2, bounds.Width / bands - 1);
            int gap = Math.Max(1, bounds.Width / bands - barWidth);
            int totalPerBar = barWidth + gap;

            for (int i = 0; i < bands; i++)
            {
                float val = spectrum[i] * 4f;
                val = Math.Min(1f, val);
                _smoothed[i] = _smoothed[i] * (1f - BarDecay) + val * BarDecay;

                float height = _smoothed[i] * bounds.Height;
                int x = bounds.Left + i * totalPerBar;
                int y = bounds.Bottom - (int)height;
                int h = (int)height;

                if (h > 1)
                {
                    using var brush = new LinearGradientBrush(
                        new Point(x, y), new Point(x, bounds.Bottom),
                        BarColorTop, BarColorBottom);
                    g.FillRectangle(brush, x, y, barWidth, h);
                }

                if (_smoothed[i] > _peaks[i])
                {
                    _peaks[i] = _smoothed[i];
                    _peakVelocities[i] = 0f;
                }
                else
                {
                    _peakVelocities[i] += PeakDecay;
                    _peaks[i] = Math.Max(0f, _peaks[i] - _peakVelocities[i]);
                }

                int peakY = bounds.Bottom - (int)(_peaks[i] * bounds.Height);
                g.FillRectangle(new SolidBrush(PeakColor), x, peakY, barWidth, 2);
            }
        }

        public override void Reset()
        {
            Array.Clear(_peaks, 0, _peaks.Length);
            Array.Clear(_peakVelocities, 0, _peakVelocities.Length);
            Array.Clear(_smoothed, 0, _smoothed.Length);
        }
    }
}
