using System;
using System.Drawing;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class TimeDisplay : Label
    {
        private TimeSpan _current;
        private TimeSpan _total;

        public TimeDisplay()
        {
            Font = new Font("Consolas", 10f);
            ForeColor = Color.White;
            BackColor = Color.Transparent;
            TextAlign = ContentAlignment.MiddleRight;
            AutoSize = false;
            Size = new Size(140, 20);
            UpdateText();
        }

        public void Update(TimeSpan current, TimeSpan total)
        {
            _current = current;
            _total = total;
            UpdateText();
        }

        private void UpdateText()
        {
            Text = $"{FormatTime(_current)} / {FormatTime(_total)}";
        }

        private static string FormatTime(TimeSpan t)
        {
            return t.Hours > 0
                ? $"{t.Hours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes}:{t.Seconds:D2}";
        }
    }
}
