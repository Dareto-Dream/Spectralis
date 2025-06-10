using System;
using Spectralis.Audio;

namespace Spectralis.Queue
{
    public class QueueAutoAdvance : IDisposable
    {
        private readonly PlayQueue _queue;
        private readonly AudioEngine _engine;
        private bool _disposed;

        public event EventHandler<PlayQueueItem> TrackStarted;

        public QueueAutoAdvance(PlayQueue queue, AudioEngine engine)
        {
            _queue = queue;
            _engine = engine;
            _engine.TrackEnded += OnTrackEnded;
        }

        private void OnTrackEnded(object sender, EventArgs e)
        {
            var next = _queue.Next();
            if (next == null) return;

            try
            {
                _engine.Load(next.Track.FilePath);
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
