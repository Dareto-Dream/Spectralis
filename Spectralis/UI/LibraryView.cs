using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Library;

namespace Spectralis.UI
{
    public class LibraryView : Panel
    {
        private readonly DataGridView _grid;
        private readonly TextBox _searchBox;
        private readonly ComboBox _sortCombo;
        private IList<LibraryTrack> _tracks;

        public event EventHandler<LibraryTrack> TrackActivated;
        public event EventHandler<LibraryTrack> TrackAddToQueue;

        public LibraryView()
        {
            BackColor = Color.FromArgb(18, 18, 22);

            _searchBox = new TextBox
            {
                Dock = DockStyle.None,
                Location = new Point(4, 4),
                Size = new Size(300, 24),
                BackColor = Color.FromArgb(35, 35, 44),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "Search library..."
            };
            _searchBox.TextChanged += OnSearchChanged;

            _sortCombo = new ComboBox
            {
                Location = new Point(312, 4),
                Size = new Size(140, 24),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(35, 35, 44),
                ForeColor = Color.White
            };
            _sortCombo.Items.AddRange(new object[] { "Artist", "Album", "Title", "Year", "Date Added", "Play Count" });
            _sortCombo.SelectedIndex = 0;

            _grid = new DataGridView
            {
                Location = new Point(0, 34),
                BackgroundColor = Color.FromArgb(18, 18, 22),
                ForeColor = Color.White,
                GridColor = Color.FromArgb(35, 35, 44),
                BorderStyle = BorderStyle.None,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersDefaultCellStyle = { BackColor = Color.FromArgb(26, 26, 32), ForeColor = Color.White },
                DefaultCellStyle = { BackColor = Color.FromArgb(18, 18, 22), ForeColor = Color.White, SelectionBackColor = Color.FromArgb(50, 70, 120), SelectionForeColor = Color.White },
                AlternatingRowsDefaultCellStyle = { BackColor = Color.FromArgb(22, 22, 28) }
            };

            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "Title", FillWeight = 35 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Artist", HeaderText = "Artist", FillWeight = 25 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Album", HeaderText = "Album", FillWeight = 25 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Duration", HeaderText = "Time", FillWeight = 8 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Year", HeaderText = "Year", FillWeight = 7 });

            _grid.CellDoubleClick += OnCellDoubleClick;

            Controls.AddRange(new Control[] { _searchBox, _sortCombo, _grid });
            Resize += (s, e) => LayoutControls();
        }

        private void LayoutControls()
        {
            _grid.Size = new Size(Width, Height - 34);
        }

        public void LoadTracks(IList<LibraryTrack> tracks)
        {
            _tracks = tracks;
            PopulateGrid(tracks);
        }

        private void PopulateGrid(IList<LibraryTrack> tracks)
        {
            _grid.Rows.Clear();
            foreach (var t in tracks)
            {
                _grid.Rows.Add(t.DisplayTitle, t.DisplayArtist, t.Album ?? "",
                    FormatDuration(t.Duration), t.Year > 0 ? t.Year.ToString() : "");
            }
        }

        private void OnSearchChanged(object sender, EventArgs e)
        {
            if (_tracks == null) return;
            var q = _searchBox.Text.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(q)) { PopulateGrid(_tracks); return; }
            var filtered = new List<LibraryTrack>();
            foreach (var t in _tracks)
                if ((t.Title ?? "").ToLowerInvariant().Contains(q) ||
                    (t.Artist ?? "").ToLowerInvariant().Contains(q) ||
                    (t.Album ?? "").ToLowerInvariant().Contains(q))
                    filtered.Add(t);
            PopulateGrid(filtered);
        }

        private void OnCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || _tracks == null || e.RowIndex >= _tracks.Count) return;
            TrackActivated?.Invoke(this, _tracks[e.RowIndex]);
        }

        private static string FormatDuration(TimeSpan t) =>
            t.Hours > 0 ? $"{t.Hours}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes}:{t.Seconds:D2}";
    }
}
