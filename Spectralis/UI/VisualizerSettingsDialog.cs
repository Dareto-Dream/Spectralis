using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Visualizers;

namespace Spectralis.UI
{
    public class VisualizerSettingsDialog : Form
    {
        private readonly VisualizerSettings _settings;
        private TrackBar _tbSensitivity;
        private TrackBar _tbSmoothing;
        private CheckBox _chkPeaks;
        private CheckBox _chkMirror;
        private NumericUpDown _nudBarCount;
        private Button _btnOk;
        private Button _btnCancel;

        public VisualizerSettingsDialog(VisualizerSettings settings)
        {
            _settings = settings.Clone();
            Text = "Visualizer Settings";
            Size = new Size(360, 280);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(10)
            };

            layout.Controls.Add(new Label { Text = "Bar count:", AutoSize = true }, 0, 0);
            _nudBarCount = new NumericUpDown { Minimum = 16, Maximum = 256, Value = settings.BarCount };
            layout.Controls.Add(_nudBarCount, 1, 0);

            layout.Controls.Add(new Label { Text = "Sensitivity:", AutoSize = true }, 0, 1);
            _tbSensitivity = new TrackBar { Minimum = 1, Maximum = 40, Value = (int)(settings.Sensitivity * 10), TickFrequency = 5, AutoSize = false, Height = 30, Width = 200 };
            layout.Controls.Add(_tbSensitivity, 1, 1);

            layout.Controls.Add(new Label { Text = "Smoothing:", AutoSize = true }, 0, 2);
            _tbSmoothing = new TrackBar { Minimum = 1, Maximum = 50, Value = (int)(settings.SmoothingFactor * 100), TickFrequency = 5, AutoSize = false, Height = 30, Width = 200 };
            layout.Controls.Add(_tbSmoothing, 1, 2);

            _chkPeaks = new CheckBox { Text = "Show peaks", Checked = settings.ShowPeaks, AutoSize = true };
            layout.Controls.Add(_chkPeaks, 0, 3);
            layout.SetColumnSpan(_chkPeaks, 2);

            _chkMirror = new CheckBox { Text = "Mirror mode", Checked = settings.MirrorMode, AutoSize = true };
            layout.Controls.Add(_chkMirror, 0, 4);
            layout.SetColumnSpan(_chkMirror, 2);

            var btnPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
            _btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            _btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK };
            btnPanel.Controls.AddRange(new Control[] { _btnCancel, _btnOk });
            layout.Controls.Add(btnPanel, 0, 5);
            layout.SetColumnSpan(btnPanel, 2);

            Controls.Add(layout);
            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        public VisualizerSettings GetSettings()
        {
            _settings.BarCount = (int)_nudBarCount.Value;
            _settings.Sensitivity = _tbSensitivity.Value / 10f;
            _settings.SmoothingFactor = _tbSmoothing.Value / 100f;
            _settings.ShowPeaks = _chkPeaks.Checked;
            _settings.MirrorMode = _chkMirror.Checked;
            return _settings;
        }
    }
}
