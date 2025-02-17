using System;
using NAudio.Vorbis;
using NAudio.Wave;

namespace Spectralis.Audio.FormatReaders
{
    public class OggReader : IAudioReader
    {
        private readonly VorbisWaveReader _inner;

        public OggReader(string filePath)
        {
            _inner = new VorbisWaveReader(filePath);
        }

        public WaveFormat WaveFormat => _inner.WaveFormat;
        public TimeSpan Duration => _inner.TotalTime;
        public string SupportedExtension => ".ogg";

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
