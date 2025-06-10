using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Library;

namespace Spectralis.UI
{
    public class NowPlayingBar : Panel
    {
        private readonly Label _lblTitle;
        private readonly Label _lblArtist;
        private readonly PictureBox _cover;
        private readonly TrackBar _seekBar;
        private readonly Label _lblTime;
        private readonly Label _lblDuration;
        private bool _suppressSeek;

        public event EventHandler<double> SeekRequested;

        public NowPlayingBar()
        {
            Height = 72;
            BackColor = Color.FromArgb(14, 14, 20);
            Dock = DockStyle.Bottom;

            _cover = new PictureBox
            {
                Width = 60,
                Height = 60,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(30, 30, 40),
                Location = new Point(6, 6)
            };

            _lblTitle = new Label
            {
                AutoSize = false,
                Width = 260,
                Height = 18,
                Location = new Point(72, 6),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _lblArtist = new Label
            {
                AutoSize = false,
                Width = 260,
                Height = 16,
                Location = new Point(72, 24),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(140, 140, 160),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _seekBar = new TrackBar
            {
                Minimum = 0,
                Maximum = 1000,
                TickFrequency = 0,
                Width = 260,
                Height = 24,
                Location = new Point(72, 42),
                TickStyle = TickStyle.None
            };

            _lblTime = new Label
            {
                AutoSize = false,
                Width = 42,
                Height = 16,
                Location = new Point(336, 46),
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(120, 120, 130),
                TextAlign = ContentAlignment.MiddleRight,
                Text = "0:00"
            };

            _lblDuration = new Label
            {
                AutoSize = false,
                Width = 42,
                Height = 16,
                Location = new Point(380, 46),
                Font = new Font("Segoe UI", 7.5f),
                ForeColor = Color.FromArgb(100, 100, 110),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "/ 0:00"
            };

            _seekBar.Scroll += (s, e) =>
            {
                if (_suppressSeek) return;
                double pct = _seekBar.Value / 1000.0;
                SeekRequested?.Invoke(this, pct);
            };

            Controls.AddRange(new Control[] { _cover, _lblTitle, _lblArtist, _seekBar, _lblTime, _lblDuration });
        }

        public void ShowTrack(TrackInfo track, Image cover = null)
        {
            _lblTitle.Text = track?.Title ?? string.Empty;
            _lblArtist.Text = track?.Artist ?? string.Empty;
            _cover.Image = cover;
            _suppressSeek = true;
            _seekBar.Value = 0;
            _suppressSeek = false;
            _lblDuration.Text = "/ " + (track?.Duration.ToString(@"m\:ss") ?? "0:00");
        }

        public void UpdatePosition(TimeSpan position, TimeSpan duration)
        {
            _suppressSeek = true;
            if (duration.TotalSeconds > 0)
                _seekBar.Value = (int)(position.TotalSeconds / duration.TotalSeconds * 1000);
            _suppressSeek = false;
            _lblTime.Text = position.ToString(@"m\:ss");
        }
    }
}
