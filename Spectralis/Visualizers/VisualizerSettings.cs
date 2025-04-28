using System.Drawing;

namespace Spectralis.Visualizers
{
    public class VisualizerSettings
    {
        public int BarCount { get; set; } = 64;
        public int BarGap { get; set; } = 1;
        public float Sensitivity { get; set; } = 1.0f;
        public float SmoothingFactor { get; set; } = 0.12f;
        public bool ShowPeaks { get; set; } = true;
        public bool MirrorMode { get; set; } = false;
        public Color PrimaryColor { get; set; } = Color.FromArgb(0, 200, 255);
        public Color SecondaryColor { get; set; } = Color.FromArgb(0, 80, 160);
        public Color PeakColor { get; set; } = Color.FromArgb(255, 80, 80);
        public Color BackgroundColor { get; set; } = Color.FromArgb(10, 10, 15);

        public VisualizerSettings Clone() => (VisualizerSettings)MemberwiseClone();
    }
}
