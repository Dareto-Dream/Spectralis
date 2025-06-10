using System;
using NAudio.Wave;

namespace Spectralis.Audio.FormatReaders
{
    public class WavReader : IAudioReader
    {
        private readonly WaveFileReader _inner;

        public WavReader(string filePath)
        {
            _inner = new WaveFileReader(filePath);
        }

        public WaveFormat WaveFormat => _inner.WaveFormat;
        public TimeSpan Duration => _inner.TotalTime;
        public string SupportedExtension => ".wav";

        public TimeSpan Position
        {
            get => _inner.CurrentTime;
            set => _inner.CurrentTime = value;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public void Dispose() => _inner?.Dispose();
    }
}
