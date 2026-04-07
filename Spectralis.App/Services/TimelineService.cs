using System;
using System.Threading.Tasks;
using Spectralis.Core.Timeline;

namespace Spectralis.App.Services
{
    public class TimelineService : IDisposable
    {
        private readonly TimelineSerializer _serializer = new();
        private readonly TimelineRuntime _runtime = new();

        public event EventHandler<TimelineEvent>? EventStarted
        {
            add => _runtime.EventStarted += value;
            remove => _runtime.EventStarted -= value;
        }

        public event EventHandler<TimelineEvent>? EventEnded
        {
            add => _runtime.EventEnded += value;
            remove => _runtime.EventEnded -= value;
        }

        public ReactiveTimeline? Current { get; private set; }

        public async Task LoadAsync(string path, Func<TimeSpan> positionGetter)
        {
            Unload();
            var timeline = await _serializer.LoadAsync(path);
            if (timeline != null)
            {
                Current = timeline;
                _runtime.Load(timeline, positionGetter);
            }
        }

        public void Unload()
        {
            _runtime.Unload();
            Current = null;
        }

        public async Task SaveAsync(string path)
        {
            if (Current != null)
                await _serializer.SaveAsync(Current, path);
        }

        public void Dispose()
        {
            _runtime.Unload();
            _runtime.Dispose();
        }
    }
}
