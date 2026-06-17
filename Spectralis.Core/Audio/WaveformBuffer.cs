using System;

namespace Spectralis.Core.Audio
{
    public class WaveformBuffer
    {
        private readonly float[] _buffer;
        private int _writePos;
        private readonly object _lock = new object();

        public int Capacity => _buffer.Length;

        public WaveformBuffer(int size = 2048)
        {
            _buffer = new float[size];
        }

        public void Write(float[] samples, int offset, int count)
        {
            lock (_lock)
            {
                for (int i = 0; i < count; i++)
                {
                    _buffer[_writePos] = samples[offset + i];
                    _writePos = (_writePos + 1) % _buffer.Length;
                }
            }
        }

        public float[] CopySnapshot()
        {
            lock (_lock)
            {
                var snap = new float[_buffer.Length];
                int start = _writePos;
                for (int i = 0; i < _buffer.Length; i++)
                    snap[i] = _buffer[(start + i) % _buffer.Length];
                return snap;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _writePos = 0;
            }
        }
    }
}
