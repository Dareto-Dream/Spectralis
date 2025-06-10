using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace Spectralis.Visualizers
{
    public class FrequencyHeatmap : VisualizerBase
    {
        public override string Name => "Frequency Heatmap";

        private Bitmap _heatmap;
        private int _scrollX;
        private readonly float[] _smoothed;
        private const float Decay = 0.2f;

        public FrequencyHeatmap(int bands = 64)
        {
            _smoothed = new float[bands];
        }

        public override void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform)
        {
            EnsureHeatmap(bounds.Width, bounds.Height);

            if (spectrum != null)
            {
                int bands = Math.Min(spectrum.Length, _smoothed.Length);
                ScrollLeft();

                for (int i = 0; i < bands; i++)
                {
                    float val = Math.Min(1f, spectrum[i] * 4f);
                    _smoothed[i] = _smoothed[i] * (1f - Decay) + val * Decay;

                    int y = (int)((float)i / bands * _heatmap.Height);
                    int h = Math.Max(1, _heatmap.Height / bands);
                    Color c = HeatColor(_smoothed[i]);

                    for (int row = y; row < Math.Min(y + h, _heatmap.Height); row++)
                        _heatmap.SetPixel(_scrollX, row, c);
                }
            }

            g.DrawImage(_heatmap, bounds.Location);
        }

        private void EnsureHeatmap(int w, int h)
        {
            if (_heatmap != null && _heatmap.Width == w && _heatmap.Height == h) return;
            _heatmap?.Dispose();
            _heatmap = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
            _scrollX = 0;
        }

        private void ScrollLeft()
        {
            _scrollX = (_scrollX + 1) % _heatmap.Width;
        }

        private static Color HeatColor(float v)
        {
            if (v < 0.25f) return Color.FromArgb((int)(v * 4 * 255), 0, 0, 80);
            if (v < 0.5f) return Color.FromArgb(255, 0, (int)((v - 0.25f) * 4 * 180), 200);
            if (v < 0.75f) return Color.FromArgb(255, (int)((v - 0.5f) * 4 * 200), 200, 0);
            return Color.FromArgb(255, 255, (int)((1f - v) * 4 * 255), 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _heatmap?.Dispose();
            base.Dispose(disposing);
        }

        public override void Reset()
        {
            _heatmap?.Dispose();
            _heatmap = null;
            Array.Clear(_smoothed, 0, _smoothed.Length);
        }
    }
}
