using System;
using NAudio.Wave;

namespace Spectralis.Core.Audio.FormatReaders
{
    public interface IAudioReader : IDisposable
    {
        WaveFormat WaveFormat { get; }
        TimeSpan TotalTime { get; }
        TimeSpan CurrentTime { get; set; }
        int Read(byte[] buffer, int offset, int count);
    }
}
