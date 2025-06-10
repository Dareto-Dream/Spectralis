using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Spectralis.Library
{
    public class LibraryWatcherDebouncer : IDisposable
    {
        private readonly ConcurrentDictionary<string, Timer> _pendingEvents = new ConcurrentDictionary<string, Timer>();
        private readonly Action<string> _handler;
        private readonly int _delayMs;

        public LibraryWatcherDebouncer(Action<string> handler, int delayMs = 500)
        {
            _handler = handler;
            _delayMs = delayMs;
        }

        public void Trigger(string path)
        {
            if (_pendingEvents.TryGetValue(path, out var existing))
            {
                existing.Change(_delayMs, Timeout.Infinite);
                return;
            }

            var timer = new Timer(_ =>
            {
                _pendingEvents.TryRemove(path, out _);
                _handler(path);
            }, null, _delayMs, Timeout.Infinite);

            _pendingEvents[path] = timer;
        }

        public void Dispose()
        {
            foreach (var t in _pendingEvents.Values)
                t.Dispose();
            _pendingEvents.Clear();
        }
    }
}
