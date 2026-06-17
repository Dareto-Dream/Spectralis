using System;
using System.Threading.Tasks;
using Spectralis.Core.Models;
using Spectralis.Core.Queue;

namespace Spectralis.App.Services
{
    public class QueueService : IDisposable
    {
        private readonly PlayQueue _queue;
        private readonly QueuePersistence _persistence;
        private bool _dirty;

        public QueueService(PlayQueue queue, QueuePersistence persistence)
        {
            _queue = queue;
            _persistence = persistence;
            _queue.QueueChanged += OnQueueChanged;
        }

        public PlayQueue Queue => _queue;

        public async Task LoadSessionAsync()
        {
            var snapshot = await _persistence.LoadAsync();
            if (snapshot != null) _persistence.RestoreInto(_queue, snapshot);
        }

        public async Task SaveSessionAsync()
        {
            if (!_dirty) return;
            await _persistence.SaveAsync(_queue);
            _dirty = false;
        }

        public void EnqueueNext(TrackInfo track)
        {
            int insertAt = _queue.CurrentIndex + 1;
            if (insertAt <= 0 || insertAt > _queue.Count)
            {
                _queue.Add(new PlayQueueItem(track));
                return;
            }
            var item = new PlayQueueItem(track);
            _queue.Add(item);
            if (_queue.Count > 1)
                _queue.Move(_queue.Count - 1, insertAt);
        }

        private void OnQueueChanged(object? sender, EventArgs e) => _dirty = true;

        public void Dispose() => _queue.QueueChanged -= OnQueueChanged;
    }
}
