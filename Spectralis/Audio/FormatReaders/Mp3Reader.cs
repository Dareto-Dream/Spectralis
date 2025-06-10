using System;
using NAudio.Wave;

namespace Spectralis.Audio.FormatReaders
{
    public class Mp3Reader : IAudioReader
    {
        private readonly Mp3FileReader _inner;
        private readonly Mp3WaveFormat _mp3Format;

        public Mp3Reader(string filePath)
        {
            _inner = new Mp3FileReader(filePath);
            _mp3Format = _inner.Mp3WaveFormat;
        }

        public WaveFormat WaveFormat => _inner.WaveFormat;
        public TimeSpan Duration => _inner.TotalTime;
        public string SupportedExtension => ".mp3";

        public TimeSpan Position
        {
            get => _inner.CurrentTime;
            set
            {
                var clamped = TimeSpan.FromSeconds(Math.Max(0, Math.Min(value.TotalSeconds, _inner.TotalTime.TotalSeconds)));
                _inner.CurrentTime = clamped;
            }
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            return _inner.Read(buffer, offset, count);
        }

        public int Bitrate => _mp3Format?.AverageBytesPerSecond * 8 / 1000 ?? 0;

        public void Dispose() => _inner?.Dispose();
    }
}
