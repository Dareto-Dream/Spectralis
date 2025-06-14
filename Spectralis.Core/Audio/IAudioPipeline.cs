using System;

namespace Spectralis.Core.Audio
{
    public readonly struct AudioFrame
    {
        public readonly float[] Spectrum;
        public readonly float[] Waveform;
        public readonly float RmsLeft;
        public readonly float RmsRight;
        public readonly float PeakLeft;
        public readonly float PeakRight;
        public readonly TimeSpan Timestamp;

        public AudioFrame(float[] spectrum, float[] waveform, float rmsL, float rmsR, float peakL, float peakR, TimeSpan ts)
        {
            Spectrum = spectrum;
            Waveform = waveform;
            RmsLeft = rmsL;
            RmsRight = rmsR;
            PeakLeft = peakL;
            PeakRight = peakR;
            Timestamp = ts;
        }
    }

    public interface IAudioPipeline : IDisposable
    {
        event EventHandler<AudioFrame> FrameReady;
        bool IsActive { get; }
        void Start();
        void Stop();
        int BandCount { get; }
    }
}
