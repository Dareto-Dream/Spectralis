using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Spectralis.Library;

namespace Spectralis.UI
{
    public class ArtistBrowser : Panel
    {
        private readonly ListBox _artistList;
        private readonly ListBox _albumList;
        public event EventHandler<string> ArtistSelected;
        public event EventHandler<(string Artist, string Album)> AlbumSelected;

        public ArtistBrowser()
        {
            BackColor = Color.FromArgb(22, 22, 28);
            Width = 220;

            var lblArtists = new Label { Text = "Artists", Location = new Point(4, 4), AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8f, FontStyle.Bold) };

            _artistList = new ListBox
            {
                Location = new Point(0, 22),
                Size = new Size(220, 200),
                BackColor = Color.FromArgb(22, 22, 28),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None
            };

            var lblAlbums = new Label { Text = "Albums", Location = new Point(4, 228), AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8f, FontStyle.Bold) };

            _albumList = new ListBox
            {
                Location = new Point(0, 246),
                Size = new Size(220, 200),
                BackColor = Color.FromArgb(22, 22, 28),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None
            };

            _artistList.SelectedIndexChanged += OnArtistChanged;
            _albumList.SelectedIndexChanged += OnAlbumChanged;

            Controls.AddRange(new Control[] { lblArtists, _artistList, lblAlbums, _albumList });
        }

        public void LoadArtists(IEnumerable<string> artists)
        {
            _artistList.BeginUpdate();
            _artistList.Items.Clear();
            _artistList.Items.Add("All Artists");
            foreach (var a in artists.OrderBy(x => x))
                _artistList.Items.Add(a);
            _artistList.SelectedIndex = 0;
            _artistList.EndUpdate();
        }

        public void LoadAlbums(IEnumerable<string> albums)
        {
            _albumList.BeginUpdate();
            _albumList.Items.Clear();
            _albumList.Items.Add("All Albums");
            foreach (var a in albums.OrderBy(x => x))
                _albumList.Items.Add(a);
            _albumList.SelectedIndex = 0;
            _albumList.EndUpdate();
        }

        private void OnArtistChanged(object sender, EventArgs e)
        {
            string artist = _artistList.SelectedIndex <= 0 ? null : _artistList.SelectedItem?.ToString();
            ArtistSelected?.Invoke(this, artist);
        }

        private void OnAlbumChanged(object sender, EventArgs e)
        {
            string artist = _artistList.SelectedIndex <= 0 ? null : _artistList.SelectedItem?.ToString();
            string album = _albumList.SelectedIndex <= 0 ? null : _albumList.SelectedItem?.ToString();
            if (album != null)
                AlbumSelected?.Invoke(this, (artist, album));
        }
    }
}
