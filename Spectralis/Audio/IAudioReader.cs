using System;
using NAudio.Wave;

namespace Spectralis.Audio
{
    public interface IAudioReader : IDisposable
    {
        WaveFormat WaveFormat { get; }
        TimeSpan Duration { get; }
        TimeSpan Position { get; set; }
        int Read(byte[] buffer, int offset, int count);
        string SupportedExtension { get; }
    }
}
