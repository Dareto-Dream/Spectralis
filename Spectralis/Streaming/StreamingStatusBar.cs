using System;
using System.Drawing;
using System.Windows.Forms;

namespace Spectralis.Streaming
{
    public class StreamingStatusBar : Panel
    {
        private readonly Label _lblSource;
        private readonly Label _lblTrack;
        private readonly Label _lblStatus;
        private readonly PictureBox _thumbnail;

        public StreamingStatusBar()
        {
            Height = 48;
            BackColor = Color.FromArgb(22, 22, 28);
            Dock = DockStyle.Bottom;

            _thumbnail = new PictureBox
            {
                Width = 44,
                Height = 44,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(40, 40, 50),
                Location = new Point(2, 2)
            };

            _lblSource = new Label
            {
                AutoSize = false,
                Width = 70,
                Height = 16,
                Location = new Point(50, 4),
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 160, 255),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _lblTrack = new Label
            {
                AutoSize = false,
                Width = 320,
                Height = 16,
                Location = new Point(50, 20),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(220, 220, 220),
                TextAlign = ContentAlignment.MiddleLeft
            };

            _lblStatus = new Label
            {
                AutoSize = false,
                Width = 300,
                Height = 14,
                Location = new Point(50, 36),
                Font = new Font("Segoe UI", 7f),
                ForeColor = Color.FromArgb(100, 100, 110),
                TextAlign = ContentAlignment.MiddleLeft
            };

            Controls.AddRange(new Control[] { _thumbnail, _lblSource, _lblTrack, _lblStatus });
        }

        public void ShowTrack(StreamingTrack track)
        {
            if (InvokeRequired) { Invoke(new Action(() => ShowTrack(track))); return; }
            _lblSource.Text = track.Source?.ToUpper();
            _lblTrack.Text = $"{track.Artist} — {track.Title}";
            _lblStatus.Text = string.Empty;
        }

        public void ShowStatus(string status)
        {
            if (InvokeRequired) { Invoke(new Action(() => ShowStatus(status))); return; }
            _lblStatus.Text = status;
        }

        public void Clear()
        {
            if (InvokeRequired) { Invoke(new Action(Clear)); return; }
            _lblSource.Text = string.Empty;
            _lblTrack.Text = string.Empty;
            _lblStatus.Text = string.Empty;
            _thumbnail.Image = null;
        }
    }
}
