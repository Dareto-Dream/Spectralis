using System;
using System.Collections.Generic;

namespace Spectralis.Audio
{
    public class Playlist
    {
        private readonly List<TrackInfo> _tracks = new List<TrackInfo>();
        private int _currentIndex = -1;

        public IReadOnlyList<TrackInfo> Tracks => _tracks;
        public TrackInfo CurrentTrack => _currentIndex >= 0 && _currentIndex < _tracks.Count ? _tracks[_currentIndex] : null;
        public int CurrentIndex => _currentIndex;
        public int Count => _tracks.Count;

        public void Add(TrackInfo track)
        {
            _tracks.Add(track);
        }

        public void AddRange(IEnumerable<TrackInfo> tracks)
        {
            _tracks.AddRange(tracks);
        }

        public void Clear()
        {
            _tracks.Clear();
            _currentIndex = -1;
        }

        public void Remove(int index)
        {
            if (index < 0 || index >= _tracks.Count) return;
            _tracks.RemoveAt(index);
            if (_currentIndex >= _tracks.Count)
                _currentIndex = _tracks.Count - 1;
        }

        public TrackInfo Next()
        {
            if (_tracks.Count == 0) return null;
            _currentIndex = (_currentIndex + 1) % _tracks.Count;
            return _tracks[_currentIndex];
        }

        public TrackInfo Previous()
        {
            if (_tracks.Count == 0) return null;
            _currentIndex = _currentIndex <= 0 ? _tracks.Count - 1 : _currentIndex - 1;
            return _tracks[_currentIndex];
        }

        public TrackInfo SetCurrent(int index)
        {
            if (index < 0 || index >= _tracks.Count) return null;
            _currentIndex = index;
            return _tracks[_currentIndex];
        }
    }
}
