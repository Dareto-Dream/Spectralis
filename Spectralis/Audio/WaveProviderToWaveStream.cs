using System;
using NAudio.Wave;

namespace Spectralis.Audio
{
    public class WaveProviderToWaveStream : WaveStream
    {
        private readonly IAudioReader _reader;

        public WaveProviderToWaveStream(IAudioReader reader)
        {
            _reader = reader;
        }

        public override WaveFormat WaveFormat => _reader.WaveFormat;

        public override long Length => (long)(_reader.Duration.TotalSeconds * _reader.WaveFormat.AverageBytesPerSecond);

        public override long Position
        {
            get => (long)(_reader.Position.TotalSeconds * _reader.WaveFormat.AverageBytesPerSecond);
            set => _reader.Position = TimeSpan.FromSeconds((double)value / _reader.WaveFormat.AverageBytesPerSecond);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _reader.Read(buffer, offset, count);
        }
    }
}
