using System;
using NAudio.Wave;

namespace Spectralis.Audio.FormatReaders
{
    public class FlacReader : IAudioReader
    {
        private readonly AudioFileReader _inner;

        public FlacReader(string filePath)
        {
            _inner = new AudioFileReader(filePath);
        }

        public WaveFormat WaveFormat => _inner.WaveFormat;
        public TimeSpan Duration => _inner.TotalTime;
        public string SupportedExtension => ".flac";

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
