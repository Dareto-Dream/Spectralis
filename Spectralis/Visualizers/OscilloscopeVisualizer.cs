using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Spectralis.Visualizers
{
    public class OscilloscopeVisualizer : VisualizerBase
    {
        public override string Name => "Oscilloscope";

        public Color TraceColor { get; set; } = Color.FromArgb(255, 200, 0);
        public Color GlowColor { get; set; } = Color.FromArgb(60, 255, 180, 0);
        public Color BackgroundColor { get; set; } = Color.FromArgb(5, 8, 5);

        private float _phase;

        public override void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.FillRectangle(new SolidBrush(BackgroundColor), bounds);

            DrawGrid(g, bounds);

            if (waveform == null || waveform.Length < 2) return;

            int midY = bounds.Top + bounds.Height / 2;
            float xStep = (float)bounds.Width / waveform.Length;

            int triggerPoint = FindTriggerPoint(waveform);
            var points = new PointF[waveform.Length - triggerPoint];

            for (int i = 0; i < points.Length; i++)
            {
                int idx = (i + triggerPoint) % waveform.Length;
                float x = bounds.Left + i * xStep;
                float y = midY - waveform[idx] * (bounds.Height / 2f) * 0.9f;
                y = Math.Max(bounds.Top, Math.Min(bounds.Bottom, y));
                points[i] = new PointF(x, y);
            }

            if (points.Length < 2) return;

            using var glowPen = new Pen(GlowColor, 4f);
            g.DrawLines(glowPen, points);

            using var tracePen = new Pen(TraceColor, 1.5f);
            g.DrawLines(tracePen, points);
        }

        private void DrawGrid(Graphics g, Rectangle bounds)
        {
            using var gridPen = new Pen(Color.FromArgb(20, 60, 20), 1f);
            gridPen.DashStyle = DashStyle.Dot;

            int hLines = 4;
            for (int i = 1; i < hLines; i++)
            {
                int y = bounds.Top + bounds.Height * i / hLines;
                g.DrawLine(gridPen, bounds.Left, y, bounds.Right, y);
            }

            int vLines = 8;
            for (int i = 1; i < vLines; i++)
            {
                int x = bounds.Left + bounds.Width * i / vLines;
                g.DrawLine(gridPen, x, bounds.Top, x, bounds.Bottom);
            }
        }

        private static int FindTriggerPoint(float[] waveform)
        {
            for (int i = 1; i < waveform.Length - 1; i++)
            {
                if (waveform[i - 1] < 0 && waveform[i] >= 0)
                    return i;
            }
            return 0;
        }

        public override void Reset() { _phase = 0; }
    }
}
