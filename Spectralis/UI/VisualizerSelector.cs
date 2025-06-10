using System;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class VisualizerSelector : ComboBox
    {
        public event EventHandler<string> VisualizerChanged;

        public VisualizerSelector()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            Items.AddRange(new object[]
            {
                "Spectrum Analyzer",
                "Waveform",
                "Oscilloscope",
                "VU Meter",
                "None"
            });
            SelectedIndex = 0;
            SelectedIndexChanged += OnSelectionChanged;
        }

        private void OnSelectionChanged(object sender, EventArgs e)
        {
            VisualizerChanged?.Invoke(this, SelectedItem?.ToString());
        }
    }
}
