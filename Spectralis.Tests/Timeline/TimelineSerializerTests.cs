using System;
using System.IO;
using System.Threading.Tasks;
using Spectralis.Core.Timeline;
using Xunit;

namespace Spectralis.Tests.Timeline
{
    public class TimelineSerializerTests : IDisposable
    {
        private readonly TimelineSerializer _serializer = new();
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        public TimelineSerializerTests() => Directory.CreateDirectory(_tempDir);

        [Fact]
        public async Task SaveAndLoad_RoundTrip()
        {
            var timeline = new ReactiveTimeline { Name = "Test", Duration = TimeSpan.FromSeconds(300) };
            var track = new TimelineTrack { Name = "Visual", Type = TimelineTrackType.Visual };
            track.Events.Add(new TimelineEvent
            {
                StartTime = TimeSpan.FromSeconds(10),
                Duration = TimeSpan.FromSeconds(5),
                Type = "visual.flash"
            });
            timeline.Tracks.Add(track);

            string path = Path.Combine(_tempDir, "timeline.json");
            await _serializer.SaveAsync(timeline, path);

            var loaded = await _serializer.LoadAsync(path);
            Assert.NotNull(loaded);
            Assert.Equal("Test", loaded!.Name);
            Assert.Single(loaded.Tracks);
            Assert.Single(loaded.Tracks[0].Events);
        }

        [Fact]
        public async Task Load_ReturnsNull_WhenFileNotFound()
        {
            var result = await _serializer.LoadAsync(Path.Combine(_tempDir, "missing.json"));
            Assert.Null(result);
        }

        [Fact]
        public async Task Save_WritesAtomically()
        {
            string path = Path.Combine(_tempDir, "atomic.json");
            var timeline = new ReactiveTimeline { Name = "Atomic" };
            await _serializer.SaveAsync(timeline, path);

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
