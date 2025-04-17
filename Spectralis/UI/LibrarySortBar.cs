using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Library;

namespace Spectralis.UI
{
    public class LibrarySortBar : ToolStrip
    {
        private readonly ToolStripComboBox _cmbSort;
        private readonly ToolStripButton _btnDirection;
        private SortDirection _direction = SortDirection.Ascending;

        public event EventHandler<(SortField Field, SortDirection Direction)> SortChanged;

        public LibrarySortBar()
        {
            BackColor = Color.FromArgb(30, 30, 30);
            GripStyle = ToolStripGripStyle.Hidden;
            RenderMode = ToolStripRenderMode.System;

            var lbl = new ToolStripLabel("Sort:")
            {
                ForeColor = Color.FromArgb(150, 150, 150)
            };

            _cmbSort = new ToolStripComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                AutoSize = false,
                Width = 110
            };
            _cmbSort.Items.AddRange(new object[]
            {
                "Title", "Artist", "Album", "Year", "Duration", "Play Count", "Date Added"
            });
            _cmbSort.SelectedIndex = 0;
            _cmbSort.SelectedIndexChanged += OnSortChanged;

            _btnDirection = new ToolStripButton("▲")
            {
                ToolTipText = "Ascending",
                AutoSize = true
            };
            _btnDirection.Click += OnDirectionToggle;

            Items.AddRange(new ToolStripItem[] { lbl, _cmbSort, _btnDirection });
        }

        private void OnSortChanged(object sender, EventArgs e) => RaiseSortChanged();

        private void OnDirectionToggle(object sender, EventArgs e)
        {
            _direction = _direction == SortDirection.Ascending
                ? SortDirection.Descending
                : SortDirection.Ascending;
            _btnDirection.Text = _direction == SortDirection.Ascending ? "▲" : "▼";
            _btnDirection.ToolTipText = _direction == SortDirection.Ascending ? "Ascending" : "Descending";
            RaiseSortChanged();
        }

        private void RaiseSortChanged()
        {
            var field = (SortField)_cmbSort.SelectedIndex;
            SortChanged?.Invoke(this, (field, _direction));
        }
    }
}
