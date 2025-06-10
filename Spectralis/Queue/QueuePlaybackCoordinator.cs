using System;
using Spectralis.Audio;
using Spectralis.Library;

namespace Spectralis.Queue
{
    public class QueuePlaybackCoordinator : IDisposable
    {
        private readonly PlayQueue _queue;
        private readonly AudioEngine _engine;
        private readonly QueuePersistence _persistence;
        private readonly RecentTracksStore _recents;
        private readonly QueueAutoAdvance _autoAdvance;
        private bool _disposed;

        public event EventHandler<PlayQueueItem> TrackChanged;

        public QueuePlaybackCoordinator(
            PlayQueue queue,
            AudioEngine engine,
            QueuePersistence persistence,
            RecentTracksStore recents)
        {
            _queue = queue;
            _engine = engine;
            _persistence = persistence;
            _recents = recents;

            _autoAdvance = new QueueAutoAdvance(_queue, _engine);
            _autoAdvance.TrackStarted += OnTrackStarted;
            _queue.CurrentChanged += OnCurrentChanged;
        }

        public void PlayItem(PlayQueueItem item)
        {
            if (item?.Track == null) return;
            _engine.Load(item.Track.FilePath);
            _engine.Play();
            _recents?.Record(item.Track);
            TrackChanged?.Invoke(this, item);
        }

        public void PlayNext()
        {
            var item = _queue.Next();
            if (item != null) PlayItem(item);
        }

        public void PlayPrevious()
        {
            var item = _queue.Previous();
            if (item != null) PlayItem(item);
        }

        public void SaveState() => _persistence?.Save(_queue);

        public void LoadState() => _persistence?.Load(_queue);

        private void OnTrackStarted(object sender, PlayQueueItem item)
        {
            _recents?.Record(item.Track);
            TrackChanged?.Invoke(this, item);
        }

        private void OnCurrentChanged(object sender, PlayQueueItem item)
        {
            SaveState();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _autoAdvance.Dispose();
        }
    }
}
