using System;
using NAudio.Wave;
using NAudio.MediaFoundation;

namespace Spectralis.Audio.FormatReaders
{
    public class AacReader : IAudioReader
    {
        private readonly MediaFoundationReader _inner;

        public AacReader(string filePath)
        {
            _inner = new MediaFoundationReader(filePath);
        }

        public WaveFormat WaveFormat => _inner.WaveFormat;
        public TimeSpan Duration => _inner.TotalTime;
        public string SupportedExtension => ".aac";

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
