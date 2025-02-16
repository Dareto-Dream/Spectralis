using System;
using NAudio.Wave;

namespace Spectralis.Audio.FormatReaders
{
    public class Mp3Reader : IAudioReader
    {
        private readonly Mp3FileReader _inner;

        public Mp3Reader(string filePath)
        {
            _inner = new Mp3FileReader(filePath);
        }

        public WaveFormat WaveFormat => _inner.WaveFormat;
        public TimeSpan Duration => _inner.TotalTime;
        public string SupportedExtension => ".mp3";

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
