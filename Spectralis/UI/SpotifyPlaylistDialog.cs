using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Spectralis.Streaming;

namespace Spectralis.UI
{
    public class SpotifyPlaylistDialog : Form
    {
        private readonly SpotifyPlaylistBrowser _browser;
        private readonly ListView _listPlaylists;
        private readonly ListView _listTracks;
        private readonly Button _btnLoad;
        private readonly Button _btnAddAll;
        private readonly Label _lblStatus;

        public List<StreamingTrack> SelectedTracks { get; private set; }

        public SpotifyPlaylistDialog(SpotifyPlaylistBrowser browser)
        {
            _browser = browser;
            Text = "Spotify Playlists";
            Size = new Size(760, 520);
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 280,
                Orientation = Orientation.Vertical
            };

            _listPlaylists = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                BackColor = Color.FromArgb(20, 20, 26),
                ForeColor = Color.FromArgb(210, 210, 210)
            };
            _listPlaylists.Columns.Add("Playlist", 200);
            _listPlaylists.Columns.Add("#", 50);

            _listTracks = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                BackColor = Color.FromArgb(20, 20, 26),
                ForeColor = Color.FromArgb(210, 210, 210)
            };
            _listTracks.Columns.Add("Title", 200);
            _listTracks.Columns.Add("Artist", 140);
            _listTracks.Columns.Add("Duration", 70);

            split.Panel1.Controls.Add(_listPlaylists);
            split.Panel2.Controls.Add(_listTracks);

            _lblStatus = new Label { Dock = DockStyle.Bottom, Height = 22, Text = "Loading playlists…", ForeColor = Color.FromArgb(120, 120, 130) };
            _btnLoad = new Button { Text = "Load tracks", Width = 100, Height = 26 };
            _btnAddAll = new Button { Text = "Add checked", Width = 100, Height = 26, DialogResult = DialogResult.OK };

            var btnBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
            btnBar.Controls.Add(_btnAddAll);
            btnBar.Controls.Add(_btnLoad);

            Controls.Add(split);
            Controls.Add(btnBar);
            Controls.Add(_lblStatus);

            _listPlaylists.SelectedIndexChanged += (s, e) => _ = LoadTracksAsync();
            _btnLoad.Click += (s, e) => _ = LoadTracksAsync();
            _btnAddAll.Click += OnAddAll;

            _ = LoadPlaylistsAsync();
        }

        private async Task LoadPlaylistsAsync()
        {
            try
            {
                var playlists = await _browser.GetUserPlaylistsAsync();
                _listPlaylists.Items.Clear();
                foreach (var p in playlists)
                {
                    var item = new ListViewItem(p.Name ?? "?");
                    item.SubItems.Add(p.TrackCount.ToString());
                    item.Tag = p;
                    _listPlaylists.Items.Add(item);
                }
                _lblStatus.Text = $"{playlists.Count} playlists loaded.";
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Failed: " + ex.Message;
            }
        }

        private async Task LoadTracksAsync()
        {
            if (_listPlaylists.SelectedItems.Count == 0) return;
            var playlist = (SpotifyPlaylist)_listPlaylists.SelectedItems[0].Tag;
            _lblStatus.Text = $"Loading {playlist.Name}…";

            try
            {
                var tracks = await _browser.GetPlaylistTracksAsync(playlist.Id);
                _listTracks.Items.Clear();
                foreach (var t in tracks)
                {
                    var item = new ListViewItem(t.Title ?? "?") { Checked = false };
                    item.SubItems.Add(t.Artist ?? "?");
                    item.SubItems.Add(t.Duration.ToString(@"m\:ss"));
                    item.Tag = t;
                    _listTracks.Items.Add(item);
                }
                _lblStatus.Text = $"{tracks.Count} tracks.";
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Failed: " + ex.Message;
            }
        }

        private void OnAddAll(object sender, EventArgs e)
        {
            SelectedTracks = new List<StreamingTrack>();
            foreach (ListViewItem item in _listTracks.CheckedItems)
                SelectedTracks.Add((StreamingTrack)item.Tag);
        }
    }
}
