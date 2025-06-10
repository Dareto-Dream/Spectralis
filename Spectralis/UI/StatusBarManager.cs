using System;
using System.Windows.Forms;
using Spectralis.Audio;

namespace Spectralis.UI
{
    public class StatusBarManager
    {
        private readonly ToolStripStatusLabel _label;

        public StatusBarManager(ToolStripStatusLabel label)
        {
            _label = label;
        }

        public void ShowTrack(TrackInfo info)
        {
            if (info == null) { ShowReady(); return; }

            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(info.Format)) parts.Add(info.Format);
            if (info.Bitrate > 0) parts.Add($"{info.Bitrate}kbps");
            if (info.SampleRate > 0)
            {
                string rateStr = SampleRateDetector.IsHighRes(info.SampleRate)
                    ? $"{info.SampleRate / 1000.0:F1}kHz [Hi-Res]"
                    : $"{info.SampleRate / 1000.0:F1}kHz";
                parts.Add(rateStr);
            }
            if (info.Channels == 1) parts.Add("Mono");
            else if (info.Channels == 2) parts.Add("Stereo");

            _label.Text = string.Join(" · ", parts);
        }

        public void ShowReady() => _label.Text = "Ready";

        public void ShowError(string message) => _label.Text = $"Error: {message}";

        public void ShowLoading(string fileName) => _label.Text = $"Loading {fileName}...";
    }
}
