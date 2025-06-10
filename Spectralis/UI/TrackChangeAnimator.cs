using System;
using System.Drawing;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class TrackChangeAnimator : IDisposable
    {
        private readonly Label _label;
        private readonly Timer _timer;
        private int _step;
        private const int Steps = 10;

        public TrackChangeAnimator(Label label)
        {
            _label = label;
            _timer = new Timer { Interval = 30 };
            _timer.Tick += OnTick;
        }

        public void Animate(string newText)
        {
            _label.Text = newText;
            _step = 0;
            _label.ForeColor = Color.FromArgb(0, _label.ForeColor);
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            _step++;
            int alpha = (int)(255f * _step / Steps);
            _label.ForeColor = Color.FromArgb(Math.Min(255, alpha), Color.White);
            if (_step >= Steps)
            {
                _timer.Stop();
                _label.ForeColor = Color.White;
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
