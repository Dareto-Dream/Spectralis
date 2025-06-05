using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Queue;

namespace Spectralis.UI
{
    public class QueueSearchFilter : Panel
    {
        private readonly TextBox _txtSearch;
        private readonly Label _lblClear;
        private string _lastFilter = string.Empty;

        public event EventHandler<string> FilterChanged;

        public QueueSearchFilter()
        {
            Height = 28;
            BackColor = Color.FromArgb(20, 20, 28);
            Dock = DockStyle.Top;

            _txtSearch = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 28, 38),
                ForeColor = Color.FromArgb(200, 200, 210),
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9f),
                PlaceholderText = "Filter queue…"
            };

            _lblClear = new Label
            {
                Text = "✕",
                Width = 22,
                Dock = DockStyle.Right,
                ForeColor = Color.FromArgb(100, 100, 110),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };

            Controls.Add(_txtSearch);
            Controls.Add(_lblClear);

            _txtSearch.TextChanged += (s, e) =>
            {
                string val = _txtSearch.Text.Trim();
                if (val == _lastFilter) return;
                _lastFilter = val;
                FilterChanged?.Invoke(this, val);
            };

            _lblClear.Click += (s, e) =>
            {
                _txtSearch.Text = string.Empty;
                _txtSearch.Focus();
            };
        }

        public string Filter => _lastFilter;

        public static bool Matches(PlayQueueItem item, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            string f = filter.ToLower();
            return (item.Track?.Title?.ToLower().Contains(f) ?? false)
                || (item.Track?.Artist?.ToLower().Contains(f) ?? false)
                || (item.Track?.Album?.ToLower().Contains(f) ?? false);
        }
    }
}
