using System;
using System.Drawing;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class SeekBar : Control
    {
        private double _fraction;
        private bool _isDragging;
        private readonly Brush _trackBrush = new SolidBrush(Color.FromArgb(50, 50, 60));
        private readonly Brush _fillBrush = new SolidBrush(Color.FromArgb(100, 150, 255));
        private readonly Brush _thumbBrush = new SolidBrush(Color.White);

        public event EventHandler<double> SeekRequested;

        public double Fraction
        {
            get => _fraction;
            set
            {
                if (_isDragging) return;
                _fraction = Math.Max(0.0, Math.Min(1.0, value));
                Invalidate();
            }
        }

        public SeekBar()
        {
            DoubleBuffered = true;
            Height = 18;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var trackRect = new Rectangle(0, Height / 2 - 3, Width, 6);
            g.FillRectangle(_trackBrush, trackRect);

            int fillW = (int)(Width * _fraction);
            if (fillW > 0)
                g.FillRectangle(_fillBrush, new Rectangle(0, Height / 2 - 3, fillW, 6));

            int thumbX = (int)(Width * _fraction);
            g.FillEllipse(_thumbBrush, thumbX - 6, Height / 2 - 6, 12, 12);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _isDragging = true;
            UpdateFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isDragging) UpdateFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                UpdateFromMouse(e.X);
                SeekRequested?.Invoke(this, _fraction);
            }
        }

        private void UpdateFromMouse(int x)
        {
            _fraction = Math.Max(0.0, Math.Min(1.0, (double)x / Width));
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _trackBrush?.Dispose();
                _fillBrush?.Dispose();
                _thumbBrush?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
