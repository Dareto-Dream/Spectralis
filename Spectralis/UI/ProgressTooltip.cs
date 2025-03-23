using System;
using System.Drawing;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class ProgressTooltip : IDisposable
    {
        private readonly ToolTip _tip;
        private readonly SeekBar _seekBar;
        private TimeSpan _total;

        public ProgressTooltip(SeekBar seekBar)
        {
            _seekBar = seekBar;
            _tip = new ToolTip { ShowAlways = true, AutomaticDelay = 0, AutoPopDelay = 0 };
            _seekBar.MouseMove += OnMouseMove;
        }

        public TimeSpan Total
        {
            get => _total;
            set => _total = value;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_total <= TimeSpan.Zero) return;
            double fraction = (double)e.X / _seekBar.Width;
            var t = TimeSpan.FromSeconds(fraction * _total.TotalSeconds);
            _tip.SetToolTip(_seekBar, FormatTime(t));
        }

        private static string FormatTime(TimeSpan t) =>
            t.Hours > 0 ? $"{t.Hours}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes}:{t.Seconds:D2}";

        public void Dispose()
        {
            _seekBar.MouseMove -= OnMouseMove;
            _tip?.Dispose();
        }
    }
}
