using System;
using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;
using System.IO;

namespace Spectralis.Audio.FormatReaders
{
    public class OpusReader : IAudioReader
    {
        private readonly FileStream _fileStream;
        private readonly OpusOggReadStream _oggStream;
        private readonly WaveFormat _waveFormat;
        private readonly TimeSpan _duration;
        private TimeSpan _position;
        private byte[] _pendingBuffer;
        private int _pendingOffset;

        public OpusReader(string filePath)
        {
            _fileStream = File.OpenRead(filePath);
            var decoder = OpusDecoder.Create(48000, 2);
            _oggStream = new OpusOggReadStream(decoder, _fileStream);
            _waveFormat = new WaveFormat(48000, 16, 2);
            _duration = EstimateDuration(filePath);
        }

        public WaveFormat WaveFormat => _waveFormat;
        public TimeSpan Duration => _duration;
        public string SupportedExtension => ".opus";

        public TimeSpan Position
        {
            get => _position;
            set => _position = value;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            int written = 0;

            if (_pendingBuffer != null)
            {
                int toCopy = Math.Min(count, _pendingBuffer.Length - _pendingOffset);
                Buffer.BlockCopy(_pendingBuffer, _pendingOffset, buffer, offset, toCopy);
                _pendingOffset += toCopy;
                written += toCopy;
                if (_pendingOffset >= _pendingBuffer.Length)
                    _pendingBuffer = null;
                if (written >= count) return written;
            }

            while (written < count && _oggStream.HasNextPacket)
            {
                short[] pcm = _oggStream.DecodeNextPacket();
                if (pcm == null) break;

                byte[] bytes = new byte[pcm.Length * 2];
                Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);

                int remaining = count - written;
                if (bytes.Length <= remaining)
                {
                    Buffer.BlockCopy(bytes, 0, buffer, offset + written, bytes.Length);
                    written += bytes.Length;
                }
                else
                {
                    Buffer.BlockCopy(bytes, 0, buffer, offset + written, remaining);
                    written += remaining;
                    _pendingBuffer = bytes;
                    _pendingOffset = remaining;
                    break;
                }
            }

            _position += TimeSpan.FromSeconds((double)written / _waveFormat.AverageBytesPerSecond);
            return written;
        }

        private static TimeSpan EstimateDuration(string filePath)
        {
            var info = new FileInfo(filePath);
            return TimeSpan.FromSeconds(info.Length / 12000.0);
        }

        public void Dispose()
        {
            _fileStream?.Dispose();
        }
    }
}
