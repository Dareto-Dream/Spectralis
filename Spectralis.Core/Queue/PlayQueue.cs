using System;
using System.Collections.Generic;
using System.Linq;
using Spectralis.Core.Models;

namespace Spectralis.Core.Queue
{
    public class PlayQueue
    {
        private readonly List<PlayQueueItem> _items = new List<PlayQueueItem>();
        private List<int>? _shuffleOrder;
        private int _currentIndex = -1;
        private int _shufflePos = -1;

        public event EventHandler<PlayQueueItem>? CurrentChanged;
        public event EventHandler? QueueChanged;

        public bool IsShuffled { get; private set; }
        public RepeatMode RepeatMode { get; set; } = RepeatMode.None;

        public IReadOnlyList<PlayQueueItem> Items => _items;
        public PlayQueueItem? Current => _currentIndex >= 0 && _currentIndex < _items.Count
            ? _items[_currentIndex] : null;
        public int Count => _items.Count;
        public int CurrentIndex => _currentIndex;

        public void Add(PlayQueueItem item)
        {
            _items.Add(item);
            if (IsShuffled) _shuffleOrder!.Add(_items.Count - 1);
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        public void AddRange(IEnumerable<PlayQueueItem> items)
        {
            foreach (var item in items) Add(item);
        }

        public void Remove(PlayQueueItem item)
        {
            int idx = _items.IndexOf(item);
            if (idx < 0) return;
            _items.RemoveAt(idx);
            if (idx < _currentIndex) _currentIndex--;
            else if (idx == _currentIndex) _currentIndex = Math.Min(_currentIndex, _items.Count - 1);
            if (IsShuffled) RebuildShuffleOrder();
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            _items.Clear();
            _currentIndex = -1;
            _shufflePos = -1;
            _shuffleOrder = null;
            IsShuffled = false;
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Move(int from, int to)
        {
            if (from < 0 || from >= _items.Count || to < 0 || to >= _items.Count) return;
            var item = _items[from];
            _items.RemoveAt(from);
            _items.Insert(to, item);

            if (_currentIndex == from) _currentIndex = to;
            else if (from < _currentIndex && to >= _currentIndex) _currentIndex--;
            else if (from > _currentIndex && to <= _currentIndex) _currentIndex++;

            if (IsShuffled && _shuffleOrder != null)
            {
                for (int i = 0; i < _shuffleOrder.Count; i++)
                {
                    int idx = _shuffleOrder[i];
                    if (idx == from)
                        _shuffleOrder[i] = to;
                    else if (from < to && idx > from && idx <= to)
                        _shuffleOrder[i] = idx - 1;
                    else if (from > to && idx >= to && idx < from)
                        _shuffleOrder[i] = idx + 1;
                }
            }

            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetShuffle(bool enabled)
        {
            IsShuffled = enabled;
            if (enabled) { RebuildShuffleOrder(); _shufflePos = 0; }
            else { _shuffleOrder = null; _shufflePos = -1; }
        }

        private void RebuildShuffleOrder()
        {
            var rng = new Random();
            _shuffleOrder = Enumerable.Range(0, _items.Count).OrderBy(_ => rng.Next()).ToList();
            if (_currentIndex >= 0 && _currentIndex < _items.Count)
            {
                int pos = _shuffleOrder.IndexOf(_currentIndex);
                if (pos > 0) { _shuffleOrder.RemoveAt(pos); _shuffleOrder.Insert(0, _currentIndex); }
            }
            _shufflePos = 0;
        }

        public PlayQueueItem? PlayAt(int index)
        {
            if (index < 0 || index >= _items.Count) return null;
            _currentIndex = index;
            if (IsShuffled && _shuffleOrder != null)
                _shufflePos = _shuffleOrder.IndexOf(index);
            var item = Current;
            CurrentChanged?.Invoke(this, item!);
            return item;
        }

        public PlayQueueItem? Next()
        {
            if (_items.Count == 0) return null;
            if (RepeatMode == RepeatMode.RepeatOne) return Current;

            if (IsShuffled && _shuffleOrder != null)
            {
                int next = _shufflePos + 1;
                if (next >= _shuffleOrder.Count)
                {
                    if (RepeatMode == RepeatMode.RepeatAll) { RebuildShuffleOrder(); next = 0; }
                    else return null;
                }
                _shufflePos = next;
                _currentIndex = _shuffleOrder[_shufflePos];
            }
            else
            {
                _currentIndex++;
                if (_currentIndex >= _items.Count)
                {
                    if (RepeatMode == RepeatMode.RepeatAll) _currentIndex = 0;
                    else return null;
                }
            }

            var item = Current;
            CurrentChanged?.Invoke(this, item!);
            return item;
        }

        public PlayQueueItem? Previous()
        {
            if (_items.Count == 0) return null;

            if (IsShuffled && _shuffleOrder != null)
            {
                int prev = _shufflePos - 1;
                if (prev < 0) return Current;
                _shufflePos = prev;
                _currentIndex = _shuffleOrder[_shufflePos];
            }
            else
            {
                if (_currentIndex <= 0) return Current;
                _currentIndex--;
            }

            var item = Current;
            CurrentChanged?.Invoke(this, item!);
            return item;
        }

        public bool HasNext()
        {
            if (_items.Count == 0) return false;
            if (RepeatMode != RepeatMode.None) return true;
            if (IsShuffled && _shuffleOrder != null) return _shufflePos < _shuffleOrder.Count - 1;
            return _currentIndex < _items.Count - 1;
        }
    }
}
