using System;
using System.Windows.Forms;
using Spectralis.Audio;

namespace Spectralis.UI
{
    public class SettingsDialog : Form
    {
        private readonly DeviceManager _deviceManager;
        private ComboBox _deviceCombo;
        private CheckBox _chkWasapi;
        private TrackBar _bufferBar;
        private Label _lblBuffer;
        private Button _btnOk;
        private Button _btnCancel;

        public string SelectedDeviceId { get; private set; }
        public bool UseWasapi { get; private set; }
        public int BufferMs { get; private set; }

        public SettingsDialog(DeviceManager deviceManager)
        {
            _deviceManager = deviceManager;
            Text = "Settings";
            Size = new System.Drawing.Size(420, 260);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            BuildControls();
            LoadCurrentValues();
        }

        private void BuildControls()
        {
            var lblDevice = new Label { Text = "Output Device:", Location = new System.Drawing.Point(12, 20), AutoSize = true };
            _deviceCombo = new ComboBox { Location = new System.Drawing.Point(12, 40), Size = new System.Drawing.Size(380, 24), DropDownStyle = ComboBoxStyle.DropDownList };

            _chkWasapi = new CheckBox { Text = "Use WASAPI (shared mode)", Location = new System.Drawing.Point(12, 80), AutoSize = true };

            var lblBuf = new Label { Text = "Buffer (ms):", Location = new System.Drawing.Point(12, 110), AutoSize = true };
            _bufferBar = new TrackBar { Location = new System.Drawing.Point(12, 130), Size = new System.Drawing.Size(300, 45), Minimum = 50, Maximum = 1000, TickFrequency = 50, Value = 200 };
            _lblBuffer = new Label { Location = new System.Drawing.Point(320, 140), AutoSize = true, Text = "200 ms" };
            _bufferBar.Scroll += (s, e) => _lblBuffer.Text = $"{_bufferBar.Value} ms";

            _btnOk = new Button { Text = "OK", Location = new System.Drawing.Point(220, 190), Size = new System.Drawing.Size(80, 30), DialogResult = DialogResult.OK };
            _btnCancel = new Button { Text = "Cancel", Location = new System.Drawing.Point(310, 190), Size = new System.Drawing.Size(80, 30), DialogResult = DialogResult.Cancel };

            _btnOk.Click += OnOk;

            Controls.AddRange(new Control[] { lblDevice, _deviceCombo, _chkWasapi, lblBuf, _bufferBar, _lblBuffer, _btnOk, _btnCancel });
            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private void LoadCurrentValues()
        {
            var devices = _deviceManager.GetOutputDevices();
            foreach (var d in devices)
            {
                _deviceCombo.Items.Add(d);
                if (d.IsDefault) _deviceCombo.SelectedItem = d;
            }
            _chkWasapi.Checked = true;
            _bufferBar.Value = 200;
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (_deviceCombo.SelectedItem is AudioDevice d)
                SelectedDeviceId = d.Id;
            UseWasapi = _chkWasapi.Checked;
            BufferMs = _bufferBar.Value;
        }
    }
}
