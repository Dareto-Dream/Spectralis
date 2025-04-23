using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis.Visualizers
{
    public class WaveformDisplay : VisualizerBase
    {
        public override string Name => "Waveform";

        public Color WaveColor { get; set; } = Color.FromArgb(0, 220, 120);
        public Color BackgroundColor { get; set; } = Color.FromArgb(10, 10, 15);
        public Color MidLineColor { get; set; } = Color.FromArgb(40, 40, 50);

        public override void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillRectangle(new SolidBrush(BackgroundColor), bounds);

            int midY = bounds.Top + bounds.Height / 2;
            using var midPen = new Pen(MidLineColor, 1f);
            g.DrawLine(midPen, bounds.Left, midY, bounds.Right, midY);

            if (waveform == null || waveform.Length < 2) return;

            float xStep = (float)bounds.Width / waveform.Length;
            var points = new PointF[waveform.Length];

            for (int i = 0; i < waveform.Length; i++)
            {
                float x = bounds.Left + i * xStep;
                float y = midY - waveform[i] * (bounds.Height / 2f) * 0.85f;
                y = Math.Max(bounds.Top, Math.Min(bounds.Bottom, y));
                points[i] = new PointF(x, y);
            }

            using var pen = new Pen(WaveColor, 1.5f);
            g.DrawLines(pen, points);
        }
    }
}
