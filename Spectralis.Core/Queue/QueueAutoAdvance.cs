using System;
using Spectralis.Core.Audio;

namespace Spectralis.Core.Queue
{
    public class QueueAutoAdvance : IDisposable
    {
        private readonly PlayQueue _queue;
        private readonly IAudioEngine _engine;
        private bool _disposed;

        public event EventHandler<PlayQueueItem>? TrackStarted;

        public QueueAutoAdvance(PlayQueue queue, IAudioEngine engine)
        {
            _queue = queue;
            _engine = engine;
            _engine.TrackEnded += OnTrackEnded;
        }

        private async void OnTrackEnded(object? sender, EventArgs e)
        {
            var next = _queue.Next();
            if (next == null) return;
            try
            {
                await _engine.LoadAsync(next.Track.FilePath);
                _engine.Play();
                TrackStarted?.Invoke(this, next);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _engine.TrackEnded -= OnTrackEnded;
        }
    }
}
