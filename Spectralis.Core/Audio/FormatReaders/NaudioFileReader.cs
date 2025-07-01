using System;
using NAudio.Wave;

namespace Spectralis.Core.Audio.FormatReaders
{
    public class NaudioFileReader : IAudioReader
    {
        private readonly AudioFileReader _reader;
        private bool _disposed;

        public NaudioFileReader(string filePath)
        {
            _reader = new AudioFileReader(filePath);
        }

        public WaveFormat WaveFormat => _reader.WaveFormat;
        public TimeSpan TotalTime => _reader.TotalTime;

        public TimeSpan CurrentTime
        {
            get => _reader.CurrentTime;
            set => _reader.CurrentTime = value;
        }

        public int Read(byte[] buffer, int offset, int count) =>
            _reader.Read(buffer, offset, count);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _reader.Dispose();
        }
    }
}
