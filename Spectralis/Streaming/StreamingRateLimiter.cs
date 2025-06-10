using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Spectralis.Streaming
{
    public class StreamingRateLimiter
    {
        private readonly int _maxRequests;
        private readonly TimeSpan _window;
        private readonly Queue<DateTime> _timestamps = new Queue<DateTime>();
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

        public StreamingRateLimiter(int maxRequests, TimeSpan window)
        {
            _maxRequests = maxRequests;
            _window = window;
        }

        public async Task WaitAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct);
            try
            {
                DateTime now = DateTime.UtcNow;
                DateTime cutoff = now - _window;

                while (_timestamps.Count > 0 && _timestamps.Peek() < cutoff)
                    _timestamps.Dequeue();

                if (_timestamps.Count >= _maxRequests)
                {
                    TimeSpan delay = _window - (now - _timestamps.Peek());
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, ct);
                }

                _timestamps.Enqueue(DateTime.UtcNow);
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
