using System.Drawing;

namespace Spectralis.Visualizers
{
    public class VisualizerOverlayInfo
    {
        private readonly Font _font = new Font("Segoe UI", 8f);
        private readonly Brush _textBrush = new SolidBrush(Color.FromArgb(120, 255, 255, 255));
        private readonly Brush _bgBrush = new SolidBrush(Color.FromArgb(80, 0, 0, 0));
        private bool _enabled;

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public void Draw(Graphics g, Rectangle bounds, string visualizerName, double fps)
        {
            if (!_enabled) return;

            string text = $"{visualizerName} | {fps:F0} fps";
            var size = g.MeasureString(text, _font);
            var rect = new RectangleF(bounds.Right - size.Width - 10, bounds.Top + 6, size.Width + 4, size.Height + 2);

            g.FillRectangle(_bgBrush, rect);
            g.DrawString(text, _font, _textBrush, rect.X + 2, rect.Y + 1);
        }

        public void Dispose()
        {
            _font.Dispose();
            _textBrush.Dispose();
            _bgBrush.Dispose();
        }
    }
}
