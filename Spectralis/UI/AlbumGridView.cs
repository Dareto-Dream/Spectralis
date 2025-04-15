using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Library;

namespace Spectralis.UI
{
    public class AlbumGridItem
    {
        public string Album { get; set; }
        public string Artist { get; set; }
        public int TrackCount { get; set; }
        public Image CoverArt { get; set; }
    }

    public class AlbumGridView : Panel
    {
        private readonly FlowLayoutPanel _flow;
        private const int TileSize = 120;
        private const int TilePad = 8;

        public event EventHandler<AlbumGridItem> AlbumActivated;

        public AlbumGridView()
        {
            _flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.FromArgb(20, 20, 20),
                Padding = new Padding(TilePad)
            };
            Controls.Add(_flow);
            BackColor = Color.FromArgb(20, 20, 20);
        }

        public void SetAlbums(IEnumerable<AlbumGridItem> albums)
        {
            _flow.SuspendLayout();
            foreach (Control c in _flow.Controls)
                c.Dispose();
            _flow.Controls.Clear();

            foreach (var album in albums)
                _flow.Controls.Add(CreateTile(album));

            _flow.ResumeLayout();
        }

        private Panel CreateTile(AlbumGridItem album)
        {
            var tile = new Panel
            {
                Width = TileSize,
                Height = TileSize + 36,
                Margin = new Padding(TilePad),
                BackColor = Color.FromArgb(35, 35, 35),
                Cursor = Cursors.Hand
            };

            var art = new PictureBox
            {
                Width = TileSize,
                Height = TileSize,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(50, 50, 50),
                Image = album.CoverArt
            };

            var lblAlbum = new Label
            {
                Text = album.Album ?? "Unknown Album",
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                AutoSize = false,
                Width = TileSize,
                Height = 18,
                Top = TileSize + 2,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(2, 0, 0, 0)
            };

            var lblArtist = new Label
            {
                Text = album.Artist ?? "Unknown Artist",
                ForeColor = Color.FromArgb(140, 140, 140),
                Font = new Font("Segoe UI", 7f),
                AutoSize = false,
                Width = TileSize,
                Height = 16,
                Top = TileSize + 20,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(2, 0, 0, 0)
            };

            tile.Controls.AddRange(new Control[] { art, lblAlbum, lblArtist });

            tile.Click += (s, e) => AlbumActivated?.Invoke(this, album);
            art.Click += (s, e) => AlbumActivated?.Invoke(this, album);

            return tile;
        }
    }
}
