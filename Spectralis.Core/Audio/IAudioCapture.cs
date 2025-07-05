using System;
using NAudio.Wave;

namespace Spectralis.Core.Audio
{
    public interface IAudioCapture : IDisposable
    {
        event EventHandler<WaveInEventArgs>? DataAvailable;
        event EventHandler<StoppedEventArgs>? RecordingStopped;
        WaveFormat WaveFormat { get; }
        bool IsCapturing { get; }
        void StartCapture();
        void StopCapture();
    }
}
