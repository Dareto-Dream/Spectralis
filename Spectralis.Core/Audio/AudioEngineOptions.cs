namespace Spectralis.Core.Audio
{
    public class AudioEngineOptions
    {
        public int DesiredLatencyMs { get; set; } = 200;
        public int WaveOutDeviceNumber { get; set; } = -1;
        public float InitialVolume { get; set; } = 0.8f;
        public bool EnableLoopbackCapture { get; set; } = true;
        public int FftSize { get; set; } = 2048;
        public int SpectrumBands { get; set; } = 64;
    }
}
