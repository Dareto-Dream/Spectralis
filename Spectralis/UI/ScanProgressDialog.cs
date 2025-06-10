using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Library;

namespace Spectralis.UI
{
    public class ScanProgressDialog : Form
    {
        private readonly ProgressBar _progressBar;
        private readonly Label _lblStatus;
        private readonly Label _lblCounts;
        private readonly Button _btnCancel;
        private readonly LibraryScanner _scanner;

        public ScanProgressDialog(LibraryScanner scanner)
        {
            _scanner = scanner;
            Text = "Scanning Library";
            Size = new Size(480, 180);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ControlBox = false;

            _progressBar = new ProgressBar { Location = new Point(12, 20), Size = new Size(440, 24), Style = ProgressBarStyle.Continuous };
            _lblStatus = new Label { Location = new Point(12, 52), Size = new Size(440, 20), ForeColor = Color.Gray, AutoEllipsis = true };
            _lblCounts = new Label { Location = new Point(12, 74), Size = new Size(440, 20) };
            _btnCancel = new Button { Text = "Cancel", Location = new Point(190, 108), Size = new Size(90, 30) };
            _btnCancel.Click += (s, e) => { _scanner.Cancel(); _btnCancel.Enabled = false; };

            Controls.AddRange(new Control[] { _progressBar, _lblStatus, _lblCounts, _btnCancel });

            _scanner.Progress += OnProgress;
            _scanner.ScanComplete += OnComplete;
        }

        private void OnProgress(object sender, ScanProgress p)
        {
            if (InvokeRequired) { Invoke(new Action(() => OnProgress(sender, p))); return; }
            _progressBar.Maximum = Math.Max(p.Total, 1);
            _progressBar.Value = Math.Min(p.Processed, _progressBar.Maximum);
            _lblStatus.Text = p.CurrentFile;
            _lblCounts.Text = $"Added: {p.Added}  Updated: {p.Updated}  Skipped: {p.Skipped}";
        }

        private void OnComplete(object sender, EventArgs e)
        {
            if (InvokeRequired) { Invoke(new Action(() => OnComplete(sender, e))); return; }
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _scanner.Progress -= OnProgress;
            _scanner.ScanComplete -= OnComplete;
            base.OnFormClosed(e);
        }
    }
}
