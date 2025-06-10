using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class GenreFilterPanel : Panel
    {
        private readonly CheckedListBox _genreList;
        private readonly Button _btnClear;
        private readonly Label _lblHeader;

        public event EventHandler<IReadOnlyList<string>> FilterChanged;

        public GenreFilterPanel()
        {
            _lblHeader = new Label
            {
                Text = "Genre",
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 180, 180),
                AutoSize = false,
                Height = 20,
                Dock = DockStyle.Top,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(2, 0, 0, 0)
            };

            _btnClear = new Button
            {
                Text = "Clear",
                Height = 22,
                Dock = DockStyle.Bottom,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(150, 150, 150),
                BackColor = Color.FromArgb(40, 40, 40)
            };
            _btnClear.FlatAppearance.BorderColor = Color.FromArgb(60, 60, 60);
            _btnClear.Click += (s, e) => ClearFilter();

            _genreList = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(210, 210, 210),
                BorderStyle = BorderStyle.None,
                CheckOnClick = true
            };
            _genreList.ItemCheck += OnItemCheck;

            Controls.Add(_genreList);
            Controls.Add(_btnClear);
            Controls.Add(_lblHeader);

            BackColor = Color.FromArgb(25, 25, 25);
        }

        public void SetGenres(IEnumerable<string> genres)
        {
            _genreList.ItemCheck -= OnItemCheck;
            _genreList.Items.Clear();
            foreach (var g in genres.OrderBy(x => x))
                _genreList.Items.Add(g, false);
            _genreList.ItemCheck += OnItemCheck;
        }

        public IReadOnlyList<string> ActiveGenres =>
            _genreList.CheckedItems.Cast<string>().ToList();

        public void ClearFilter()
        {
            _genreList.ItemCheck -= OnItemCheck;
            for (int i = 0; i < _genreList.Items.Count; i++)
                _genreList.SetItemChecked(i, false);
            _genreList.ItemCheck += OnItemCheck;
            FilterChanged?.Invoke(this, ActiveGenres);
        }

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            BeginInvoke(new Action(() => FilterChanged?.Invoke(this, ActiveGenres)));
        }
    }
}
