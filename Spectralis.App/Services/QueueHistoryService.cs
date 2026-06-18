using System;
using System.Collections.Generic;
using Spectralis.Core.Models;
using Spectralis.Core.Queue;

namespace Spectralis.App.Services
{
    public class QueueHistoryService
    {
        private readonly Stack<TrackInfo> _backStack = new();
        private readonly Stack<TrackInfo> _forwardStack = new();
        private readonly PlayQueue _queue;
        private const int MaxHistory = 50;

        public bool CanGoBack => _backStack.Count > 0;
        public bool CanGoForward => _forwardStack.Count > 0;

        public event EventHandler? HistoryChanged;

        public QueueHistoryService(PlayQueue queue)
        {
            _queue = queue;
            _queue.CurrentChanged += OnCurrentChanged;
        }

        private void OnCurrentChanged(object? sender, PlayQueueItem item)
        {
            if (item?.Track == null) return;
            _forwardStack.Clear();
            if (_backStack.Count >= MaxHistory) return;
            _backStack.Push(item.Track);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public void GoBack()
        {
            if (_backStack.Count < 2) return;
            var current = _backStack.Pop();
            _forwardStack.Push(current);
            var previous = _backStack.Peek();
            SeekToTrack(previous);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public void GoForward()
        {
            if (_forwardStack.Count == 0) return;
            var next = _forwardStack.Pop();
            _backStack.Push(next);
            SeekToTrack(next);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SeekToTrack(TrackInfo track)
        {
            for (int i = 0; i < _queue.Count; i++)
            {
                if (_queue.Items[i].Track.FilePath == track.FilePath)
                {
                    _queue.PlayAt(i);
                    return;
                }
            }
        }
    }
}
