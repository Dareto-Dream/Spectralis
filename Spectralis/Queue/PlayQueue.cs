using System;
using System.Collections.Generic;
using System.Linq;

namespace Spectralis.Queue
{
    public class PlayQueue
    {
        private readonly List<PlayQueueItem> _items = new List<PlayQueueItem>();
        private List<int> _shuffleOrder;
        private int _currentIndex = -1;

        public event EventHandler<PlayQueueItem> CurrentChanged;
        public event EventHandler QueueChanged;

        public bool IsShuffled { get; private set; }
        public RepeatMode RepeatMode { get; set; } = RepeatMode.None;

        public IReadOnlyList<PlayQueueItem> Items => _items;
        public PlayQueueItem Current => _currentIndex >= 0 && _currentIndex < _items.Count
            ? _items[_currentIndex] : null;

        public void Add(PlayQueueItem item)
        {
            _items.Add(item);
            if (IsShuffled) _shuffleOrder.Add(_items.Count - 1);
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
            _shuffleOrder = null;
            IsShuffled = false;
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Move(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _items.Count) return;
            if (toIndex < 0 || toIndex >= _items.Count) return;
            var item = _items[fromIndex];
            _items.RemoveAt(fromIndex);
            _items.Insert(toIndex, item);

            if (_currentIndex == fromIndex) _currentIndex = toIndex;
            else if (fromIndex < _currentIndex && toIndex >= _currentIndex) _currentIndex--;
            else if (fromIndex > _currentIndex && toIndex <= _currentIndex) _currentIndex++;

            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetShuffle(bool enabled)
        {
            IsShuffled = enabled;
            if (enabled)
                RebuildShuffleOrder();
            else
                _shuffleOrder = null;
        }

        private void RebuildShuffleOrder()
        {
            var rng = new Random();
            _shuffleOrder = Enumerable.Range(0, _items.Count).OrderBy(_ => rng.Next()).ToList();

            if (_currentIndex >= 0 && _currentIndex < _items.Count)
            {
                int pos = _shuffleOrder.IndexOf(_currentIndex);
                if (pos > 0)
                {
                    _shuffleOrder.RemoveAt(pos);
                    _shuffleOrder.Insert(0, _currentIndex);
                }
            }
        }

        public PlayQueueItem PlayAt(int index)
        {
            if (index < 0 || index >= _items.Count) return null;
            _currentIndex = index;
            var item = Current;
            CurrentChanged?.Invoke(this, item);
            return item;
        }

        public PlayQueueItem Next()
        {
            if (_items.Count == 0) return null;

            if (RepeatMode == RepeatMode.RepeatOne)
                return Current;

            if (IsShuffled && _shuffleOrder != null)
            {
                int nextPos = _currentIndex + 1;

                if (nextPos >= _shuffleOrder.Count)
                {
                    if (RepeatMode == RepeatMode.RepeatAll)
                    {
                        RebuildShuffleOrder();
                        nextPos = 0;
                    }
                    else return null;
                }

                _currentIndex = _shuffleOrder[nextPos];
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
            CurrentChanged?.Invoke(this, item);
            return item;
        }

        public PlayQueueItem Previous()
        {
            if (_items.Count == 0) return null;

            if (IsShuffled && _shuffleOrder != null)
            {
                int prevPos = _currentIndex - 1;
                if (prevPos < 0) return Current;
                _currentIndex = _shuffleOrder[prevPos];
            }
            else
            {
                if (_currentIndex <= 0) return Current;
                _currentIndex--;
            }

            var item = Current;
            CurrentChanged?.Invoke(this, item);
            return item;
        }

        public bool HasNext()
        {
            if (_items.Count == 0) return false;
            if (RepeatMode == RepeatMode.RepeatOne || RepeatMode == RepeatMode.RepeatAll) return true;
            if (IsShuffled && _shuffleOrder != null)
                return _currentIndex < _shuffleOrder.Count - 1;
            return _currentIndex < _items.Count - 1;
        }

        public int Count => _items.Count;
        public int CurrentIndex => _currentIndex;
    }
}
