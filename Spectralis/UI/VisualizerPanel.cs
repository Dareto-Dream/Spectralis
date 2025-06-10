using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Visualizers;

namespace Spectralis.UI
{
    public class VisualizerPanel : Panel
    {
        private IVisualizer _visualizer;
        private float[] _spectrum = Array.Empty<float>();
        private float[] _waveform = Array.Empty<float>();
        private Bitmap _backBuffer;
        private readonly Timer _renderTimer;
        private bool _active;

        public VisualizerPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);

            BackColor = Color.FromArgb(10, 10, 15);

            _renderTimer = new Timer { Interval = 16 };
            _renderTimer.Tick += OnRenderTick;
        }

        public void SetVisualizer(IVisualizer visualizer)
        {
            _visualizer?.Dispose();
            _visualizer = visualizer;
            _visualizer?.Reset();
        }

        public void SetData(float[] spectrum, float[] waveform)
        {
            _spectrum = spectrum ?? Array.Empty<float>();
            _waveform = waveform ?? Array.Empty<float>();
        }

        public void StartRendering()
        {
            _active = true;
            _renderTimer.Start();
        }

        public void StopRendering()
        {
            _active = false;
            _renderTimer.Stop();
            Invalidate();
        }

        private void OnRenderTick(object sender, EventArgs e)
        {
            if (_active) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_visualizer == null || !_active)
            {
                e.Graphics.FillRectangle(Brushes.Black, ClientRectangle);
                return;
            }

            EnsureBackBuffer();
            using (var g = Graphics.FromImage(_backBuffer))
                _visualizer.Render(g, new Rectangle(0, 0, _backBuffer.Width, _backBuffer.Height), _spectrum, _waveform);

            e.Graphics.DrawImage(_backBuffer, Point.Empty);
        }

        private void EnsureBackBuffer()
        {
            if (_backBuffer == null || _backBuffer.Width != Width || _backBuffer.Height != Height)
            {
                _backBuffer?.Dispose();
                _backBuffer = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _renderTimer.Dispose();
                _visualizer?.Dispose();
                _backBuffer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
