using System;
using System.Collections.Generic;
using System.Timers;

namespace Spectralis.Core.Timeline
{
    public class TimelineRuntime : IDisposable
    {
        private ReactiveTimeline? _timeline;
        private Func<TimeSpan>? _positionGetter;
        private readonly Timer _timer;
        private TimeSpan _lastPosition = TimeSpan.MinValue;
        private readonly HashSet<string> _activeEventIds = new();

        public event EventHandler<TimelineEvent>? EventStarted;
        public event EventHandler<TimelineEvent>? EventEnded;

        public TimelineRuntime(int tickMs = 16)
        {
            _timer = new Timer(tickMs);
            _timer.Elapsed += OnTick;
        }

        public void Load(ReactiveTimeline timeline, Func<TimeSpan> positionGetter)
        {
            Unload();
            _timeline = timeline;
            _positionGetter = positionGetter;
            _timer.Start();
        }

        public void Unload()
        {
            _timer.Stop();
            _timeline = null;
            _positionGetter = null;
            _lastPosition = TimeSpan.MinValue;
            _activeEventIds.Clear();
        }

        private void OnTick(object? sender, ElapsedEventArgs e)
        {
            if (_timeline == null || _positionGetter == null) return;
            var pos = _positionGetter();
            if (pos == _lastPosition) return;
            _lastPosition = pos;

            var nowActive = new HashSet<string>();
            foreach (var evt in _timeline.GetEventsAt(pos))
            {
                nowActive.Add(evt.Id);
                if (_activeEventIds.Add(evt.Id))
                    EventStarted?.Invoke(this, evt);
            }

            var ended = new List<string>();
            foreach (var id in _activeEventIds)
                if (!nowActive.Contains(id)) ended.Add(id);

            foreach (var id in ended)
            {
                _activeEventIds.Remove(id);
                var evt = FindEvent(id);
                if (evt != null) EventEnded?.Invoke(this, evt);
            }
        }

        private TimelineEvent? FindEvent(string id)
        {
            if (_timeline == null) return null;
            foreach (var track in _timeline.Tracks)
                foreach (var evt in track.Events)
                    if (evt.Id == id) return evt;
            return null;
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
        }
    }
}
