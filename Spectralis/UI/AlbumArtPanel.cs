using System;
using System.Drawing;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class AlbumArtPanel : Panel
    {
        private Image _albumArt;
        private readonly Brush _fallbackBrush = new SolidBrush(Color.FromArgb(30, 30, 35));

        public Image AlbumArt
        {
            get => _albumArt;
            set
            {
                _albumArt = value;
                Invalidate();
            }
        }

        public AlbumArtPanel()
        {
            DoubleBuffered = true;
            Size = new Size(160, 160);
            BackColor = Color.FromArgb(20, 20, 25);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (_albumArt != null)
            {
                g.DrawImage(_albumArt, new Rectangle(0, 0, Width, Height));
            }
            else
            {
                g.FillRectangle(_fallbackBrush, ClientRectangle);
                g.DrawString("No Art", Font, Brushes.Gray,
                    new RectangleF(0, 0, Width, Height),
                    new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fallbackBrush?.Dispose();
                _albumArt?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
