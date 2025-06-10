using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Streaming;

namespace Spectralis.UI
{
    public class StreamingSettingsPanel : Panel
    {
        private readonly StreamingAuthStore _store;
        private readonly CheckBox _chkAutoHistory;
        private readonly NumericUpDown _numCacheLimit;
        private readonly Label _lblCacheSize;
        private readonly Button _btnClearCache;
        private readonly Button _btnClearHistory;
        private readonly TextBox _txtYtDlpPath;
        private readonly Button _btnBrowseYtDlp;

        public event EventHandler SettingsChanged;

        public StreamingSettingsPanel(StreamingAuthStore store)
        {
            _store = store;
            Padding = new Padding(12);
            AutoSize = true;

            var layout = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                Dock = DockStyle.Fill
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            _chkAutoHistory = new CheckBox { Text = "Save streaming history", Checked = true, AutoSize = true };
            layout.Controls.Add(new Label { Text = "History:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
            layout.Controls.Add(_chkAutoHistory);

            layout.Controls.Add(new Label { Text = "Cache limit (MB):", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
            _numCacheLimit = new NumericUpDown { Minimum = 100, Maximum = 4096, Value = 512, Width = 80 };
            layout.Controls.Add(_numCacheLimit);

            layout.Controls.Add(new Label { Text = "Cache size:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
            _lblCacheSize = new Label { Text = "—", AutoSize = true };
            layout.Controls.Add(_lblCacheSize);

            _btnClearCache = new Button { Text = "Clear cache", Width = 100, Height = 26 };
            _btnClearHistory = new Button { Text = "Clear history", Width = 100, Height = 26 };
            var btnPanel = new FlowLayoutPanel { AutoSize = true };
            btnPanel.Controls.Add(_btnClearCache);
            btnPanel.Controls.Add(_btnClearHistory);
            layout.Controls.Add(new Label { Text = string.Empty });
            layout.Controls.Add(btnPanel);

            layout.Controls.Add(new Label { Text = "yt-dlp path:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft });
            var ytRow = new FlowLayoutPanel { AutoSize = true };
            _txtYtDlpPath = new TextBox { Width = 280 };
            _btnBrowseYtDlp = new Button { Text = "...", Width = 30, Height = 22 };
            ytRow.Controls.Add(_txtYtDlpPath);
            ytRow.Controls.Add(_btnBrowseYtDlp);
            layout.Controls.Add(ytRow);

            Controls.Add(layout);

            _chkAutoHistory.CheckedChanged += (s, e) => SettingsChanged?.Invoke(this, EventArgs.Empty);
            _numCacheLimit.ValueChanged += (s, e) => SettingsChanged?.Invoke(this, EventArgs.Empty);

            _btnBrowseYtDlp.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Filter = "Executable|yt-dlp.exe;yt-dlp|All|*.*", Title = "Select yt-dlp" })
                {
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        _txtYtDlpPath.Text = dlg.FileName;
                        SettingsChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            };
        }

        public bool AutoHistory => _chkAutoHistory.Checked;
        public int CacheLimitMb => (int)_numCacheLimit.Value;
        public string YtDlpPath => _txtYtDlpPath.Text;

        public void SetCacheSizeLabel(string text) => _lblCacheSize.Text = text;
    }
}
