using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Audio;

namespace Spectralis.UI
{
    public class TrackInfoDisplay : Panel
    {
        private readonly Label _lblTitle;
        private readonly Label _lblArtist;
        private readonly Label _lblAlbum;
        private readonly Label _lblFormat;

        public TrackInfoDisplay()
        {
            Size = new Size(600, 80);
            BackColor = Color.Transparent;

            _lblTitle = new Label
            {
                Location = new Point(0, 0),
                Size = new Size(600, 28),
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.White,
                Text = "No track loaded"
            };

            _lblArtist = new Label
            {
                Location = new Point(0, 28),
                Size = new Size(600, 20),
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.LightGray,
                Text = ""
            };

            _lblAlbum = new Label
            {
                Location = new Point(0, 48),
                Size = new Size(400, 18),
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.Gray,
                Text = ""
            };

            _lblFormat = new Label
            {
                Location = new Point(400, 48),
                Size = new Size(200, 18),
                Font = new Font("Consolas", 8f),
                ForeColor = Color.DimGray,
                TextAlign = ContentAlignment.MiddleRight,
                Text = ""
            };

            Controls.AddRange(new Control[] { _lblTitle, _lblArtist, _lblAlbum, _lblFormat });
        }

        public void Update(TrackInfo info)
        {
            if (info == null)
            {
                _lblTitle.Text = "No track loaded";
                _lblArtist.Text = "";
                _lblAlbum.Text = "";
                _lblFormat.Text = "";
                return;
            }

            _lblTitle.Text = info.DisplayTitle;
            _lblArtist.Text = info.DisplayArtist;
            _lblAlbum.Text = info.Album ?? "";
            _lblFormat.Text = $"{info.Format} · {info.Bitrate}kbps · {info.SampleRate / 1000.0:F1}kHz";
        }
    }
}
