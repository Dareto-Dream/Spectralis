using System;
using System.Drawing;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class VolumeKnob : Control
    {
        private float _value = 0.8f;
        private int _dragStartY;
        private float _dragStartValue;
        private readonly Pen _arcPen = new Pen(Color.FromArgb(100, 150, 255), 3f);
        private readonly Pen _trackPen = new Pen(Color.FromArgb(50, 50, 60), 3f);

        public event EventHandler<float> VolumeChanged;

        public float Value
        {
            get => _value;
            set
            {
                _value = Math.Max(0f, Math.Min(1f, value));
                Invalidate();
            }
        }

        public VolumeKnob()
        {
            DoubleBuffered = true;
            Size = new Size(48, 48);
            Cursor = Cursors.SizeNS;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var rect = new Rectangle(6, 6, Width - 12, Height - 12);
            g.DrawArc(_trackPen, rect, 135, 270);
            g.DrawArc(_arcPen, rect, 135, (int)(270 * _value));

            double angle = (135 + 270 * _value) * Math.PI / 180.0;
            int cx = Width / 2, cy = Height / 2, r = (Width - 16) / 2;
            int dx = cx + (int)(r * Math.Cos(angle));
            int dy = cy + (int)(r * Math.Sin(angle));
            g.DrawLine(Pens.White, cx, cy, dx, dy);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _dragStartY = e.Y;
            _dragStartValue = _value;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            float delta = (_dragStartY - e.Y) / 100f;
            Value = _dragStartValue + delta;
            VolumeChanged?.Invoke(this, _value);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _arcPen?.Dispose(); _trackPen?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
