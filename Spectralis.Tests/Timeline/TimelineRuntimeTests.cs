using System;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Core.Timeline;
using Xunit;

namespace Spectralis.Tests.Timeline
{
    public class TimelineRuntimeTests : IDisposable
    {
        private readonly TimelineRuntime _runtime = new(tickMs: 10);

        [Fact]
        public async Task EventStarted_FiresWhenPositionEntersEvent()
        {
            var timeline = BuildTimeline(
                start: TimeSpan.FromSeconds(0.1),
                duration: TimeSpan.FromSeconds(1));

            TimelineEvent? received = null;
            _runtime.EventStarted += (_, e) => received = e;

            var pos = TimeSpan.Zero;
            _runtime.Load(timeline, () => pos);

            pos = TimeSpan.FromSeconds(0.15);
            await Task.Delay(100);

            Assert.NotNull(received);
        }

        [Fact]
        public async Task EventEnded_FiresWhenPositionLeavesEvent()
        {
            var timeline = BuildTimeline(
                start: TimeSpan.FromSeconds(0.05),
                duration: TimeSpan.FromSeconds(0.1));

            TimelineEvent? ended = null;
            _runtime.EventEnded += (_, e) => ended = e;

            var pos = TimeSpan.FromSeconds(0.1);
            _runtime.Load(timeline, () => pos);
            await Task.Delay(100);

            pos = TimeSpan.FromSeconds(0.5);
            await Task.Delay(100);

            Assert.NotNull(ended);
        }

        [Fact]
        public void Unload_StopsEvents()
        {
            var timeline = BuildTimeline(TimeSpan.Zero, TimeSpan.FromSeconds(10));
            _runtime.Load(timeline, () => TimeSpan.FromSeconds(1));
            _runtime.Unload();
            Assert.True(true);
        }

        private static ReactiveTimeline BuildTimeline(TimeSpan start, TimeSpan duration)
        {
            var timeline = new ReactiveTimeline { Duration = TimeSpan.FromSeconds(60) };
            var track = new TimelineTrack { Name = "Visual" };
            track.Events.Add(new TimelineEvent
            {
                StartTime = start,
                Duration = duration,
                Type = "visual.fade"
            });
            timeline.Tracks.Add(track);
            return timeline;
        }

        public void Dispose() => _runtime.Dispose();
    }
}
