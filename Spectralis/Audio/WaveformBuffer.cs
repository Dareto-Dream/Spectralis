using System;

namespace Spectralis.Audio
{
    public class WaveformBuffer
    {
        private readonly float[] _buffer;
        private int _writePos;
        private readonly object _lock = new object();

        public int Capacity { get; }
        public float[] Snapshot { get; }

        public WaveformBuffer(int capacity = 2048)
        {
            Capacity = capacity;
            _buffer = new float[capacity];
            Snapshot = new float[capacity];
        }

        public void Push(float[] samples, int offset, int count)
        {
            lock (_lock)
            {
                for (int i = 0; i < count; i++)
                {
                    _buffer[_writePos] = samples[offset + i];
                    _writePos = (_writePos + 1) % Capacity;
                }
            }
        }

        public void CopySnapshot()
        {
            lock (_lock)
            {
                for (int i = 0; i < Capacity; i++)
                    Snapshot[i] = _buffer[(_writePos + i) % Capacity];
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                Array.Clear(_buffer, 0, Capacity);
                _writePos = 0;
            }
        }
    }
}
