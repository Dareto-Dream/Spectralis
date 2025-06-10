using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Spectralis.Streaming;

namespace Spectralis.UI
{
    public class StreamingSearchPanel : Panel
    {
        private readonly ComboBox _cmbSource;
        private readonly TextBox _txtQuery;
        private readonly Button _btnSearch;
        private readonly ListView _results;
        private readonly Label _lblStatus;
        private readonly StreamingRegistry _registry;
        private CancellationTokenSource _searchCts;

        public event EventHandler<StreamingTrack> TrackSelected;

        public StreamingSearchPanel(StreamingRegistry registry)
        {
            _registry = registry;

            var topBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 34,
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = false,
                BackColor = Color.FromArgb(30, 30, 30)
            };

            _cmbSource = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 110,
                Margin = new Padding(4, 4, 4, 4)
            };
            foreach (var name in registry.GetNames())
                _cmbSource.Items.Add(name);
            if (_cmbSource.Items.Count > 0) _cmbSource.SelectedIndex = 0;

            _txtQuery = new TextBox
            {
                Width = 240,
                Margin = new Padding(0, 4, 4, 4)
            };
            _txtQuery.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) _ = DoSearchAsync(); };

            _btnSearch = new Button
            {
                Text = "Search",
                Width = 70,
                Margin = new Padding(0, 4, 4, 4)
            };
            _btnSearch.Click += (s, e) => _ = DoSearchAsync();

            topBar.Controls.AddRange(new Control[] { _cmbSource, _txtQuery, _btnSearch });

            _results = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                BackColor = Color.FromArgb(20, 20, 20),
                ForeColor = Color.FromArgb(210, 210, 210)
            };
            _results.Columns.AddRange(new[]
            {
                new ColumnHeader { Text = "Title", Width = 200 },
                new ColumnHeader { Text = "Artist", Width = 140 },
                new ColumnHeader { Text = "Duration", Width = 70 }
            });
            _results.MouseDoubleClick += OnResultDoubleClick;

            _lblStatus = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 20,
                ForeColor = Color.FromArgb(120, 120, 120),
                Font = new Font("Segoe UI", 8f),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };

            Controls.Add(_results);
            Controls.Add(topBar);
            Controls.Add(_lblStatus);

            BackColor = Color.FromArgb(20, 20, 20);
        }

        private async Task DoSearchAsync()
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            string sourceName = _cmbSource.SelectedItem as string;
            if (!_registry.TryGet(sourceName, out var source)) return;

            _btnSearch.Enabled = false;
            _lblStatus.Text = "Searching...";
            _results.Items.Clear();

            try
            {
                var track = await source.SearchAsync(_txtQuery.Text, ct);
                if (track == null)
                {
                    _lblStatus.Text = "No results.";
                    return;
                }

                var item = new ListViewItem(track.Title ?? "(untitled)");
                item.SubItems.Add(track.Artist ?? "");
                item.SubItems.Add(FormatDuration(track.Duration));
                item.Tag = track;
                _results.Items.Add(item);
                _lblStatus.Text = "Done.";
            }
            catch (OperationCanceledException)
            {
                _lblStatus.Text = "Cancelled.";
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"Error: {ex.Message}";
            }
            finally
            {
                _btnSearch.Enabled = true;
            }
        }

        private void OnResultDoubleClick(object sender, MouseEventArgs e)
        {
            var item = _results.GetItemAt(e.X, e.Y);
            if (item?.Tag is StreamingTrack track)
                TrackSelected?.Invoke(this, track);
        }

        private static string FormatDuration(TimeSpan ts) =>
            ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
