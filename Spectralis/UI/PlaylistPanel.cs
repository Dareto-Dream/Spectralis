using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Audio;

namespace Spectralis.UI
{
    public class PlaylistPanel : Panel
    {
        private readonly ListBox _listBox;
        private readonly Playlist _playlist;

        public event EventHandler<int> TrackDoubleClicked;

        public PlaylistPanel(Playlist playlist)
        {
            _playlist = playlist;
            BackColor = Color.FromArgb(22, 22, 28);
            Padding = new Padding(0);

            _listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(22, 22, 28),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9f),
                SelectionMode = SelectionMode.MultiExtended,
                AllowDrop = true
            };

            _listBox.MouseDoubleClick += OnDoubleClick;
            _listBox.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            _listBox.DragDrop += OnDragDrop;

            Controls.Add(_listBox);
        }

        public void Refresh(IReadOnlyList<TrackInfo> tracks, int currentIndex)
        {
            _listBox.BeginUpdate();
            _listBox.Items.Clear();
            foreach (var t in tracks)
                _listBox.Items.Add($"{t.DisplayArtist} — {t.DisplayTitle}");
            if (currentIndex >= 0 && currentIndex < _listBox.Items.Count)
                _listBox.SelectedIndex = currentIndex;
            _listBox.EndUpdate();
        }

        private void OnDoubleClick(object sender, MouseEventArgs e)
        {
            int idx = _listBox.IndexFromPoint(e.Location);
            if (idx >= 0)
                TrackDoubleClicked?.Invoke(this, idx);
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null) return;
            foreach (var f in files)
            {
                if (FormatDetector.IsSupported(f))
                {
                    var info = MetadataExtractor.ExtractBasic(f);
                    _playlist.Add(info);
                }
            }
            Refresh(_playlist.Tracks, _playlist.CurrentIndex);
        }
    }
}
