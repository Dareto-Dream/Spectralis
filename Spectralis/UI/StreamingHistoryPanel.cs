using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Streaming;

namespace Spectralis.UI
{
    public class StreamingHistoryPanel : Panel
    {
        private readonly StreamingHistoryStore _store;
        private readonly ListView _list;
        private readonly Button _btnClear;

        public event EventHandler<StreamingTrack> TrackActivated;

        public StreamingHistoryPanel(StreamingHistoryStore store)
        {
            _store = store;
            Dock = DockStyle.Fill;

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                BackColor = Color.FromArgb(18, 18, 24),
                ForeColor = Color.FromArgb(210, 210, 210),
                Font = new Font("Segoe UI", 9f),
                BorderStyle = BorderStyle.None
            };
            _list.Columns.Add("Title", 220);
            _list.Columns.Add("Artist", 160);
            _list.Columns.Add("Source", 70);
            _list.Columns.Add("Played", 130);

            _btnClear = new Button
            {
                Text = "Clear history",
                Dock = DockStyle.Bottom,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(40, 40, 50),
                ForeColor = Color.FromArgb(180, 180, 180)
            };

            _btnClear.Click += (s, e) =>
            {
                _store.Clear();
                Reload();
            };

            _list.DoubleClick += (s, e) =>
            {
                if (_list.SelectedItems.Count == 0) return;
                var track = (StreamingTrack)_list.SelectedItems[0].Tag;
                TrackActivated?.Invoke(this, track);
            };

            Controls.Add(_list);
            Controls.Add(_btnClear);

            Reload();
        }

        public void Reload()
        {
            _list.Items.Clear();
            foreach (var entry in _store.Entries)
            {
                var item = new ListViewItem(entry.Track.Title ?? "?");
                item.SubItems.Add(entry.Track.Artist ?? "?");
                item.SubItems.Add(entry.Track.Source ?? "?");
                item.SubItems.Add(entry.PlayedAt.ToLocalTime().ToString("g"));
                item.Tag = entry.Track;
                _list.Items.Add(item);
            }
        }
    }
}
