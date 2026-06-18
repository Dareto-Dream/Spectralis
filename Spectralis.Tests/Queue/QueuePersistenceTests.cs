using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Spectralis.Core.Models;
using Spectralis.Core.Queue;
using Xunit;

namespace Spectralis.Tests.Queue
{
    public class QueuePersistenceTests
    {
        private static PlayQueueItem MakeItem(string title) =>
            new(new TrackInfo { Title = title, Artist = "Artist", FilePath = $"/music/{title}.mp3" });

        [Fact]
        public async Task SaveAndLoad_RoundTrips_Items()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            var persistence = new QueuePersistence(path);

            var queue = new PlayQueue();
            queue.Add(MakeItem("Alpha"));
            queue.Add(MakeItem("Beta"));
            queue.PlayAt(1);

            await persistence.SaveAsync(queue);
            File.Exists(path).Should().BeTrue();

            var snapshot = await persistence.LoadAsync();
            snapshot.Should().NotBeNull();
            snapshot!.Items.Should().HaveCount(2);
            snapshot.Items[0].Title.Should().Be("Alpha");
            snapshot.Items[1].Title.Should().Be("Beta");
            snapshot.CurrentIndex.Should().Be(1);

            File.Delete(path);
        }

        [Fact]
        public async Task Load_ReturnsNull_WhenFileAbsent()
        {
            var persistence = new QueuePersistence("/nonexistent/path/queue.json");
            var snap = await persistence.LoadAsync();
            snap.Should().BeNull();
        }

        [Fact]
        public async Task Save_WritesAtomically_NoPartialFile()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            var persistence = new QueuePersistence(path);
            var queue = new PlayQueue();
            queue.Add(MakeItem("Track1"));

            await persistence.SaveAsync(queue);

            File.Exists(path).Should().BeTrue();
            File.Exists(path + ".tmp").Should().BeFalse();

            File.Delete(path);
        }

        [Fact]
        public async Task RestoreInto_RecreatesTracks()
        {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            var persistence = new QueuePersistence(path);

            var original = new PlayQueue();
            original.Add(MakeItem("Song1")); original.Add(MakeItem("Song2"));
            original.PlayAt(0);
            await persistence.SaveAsync(original);

            var restored = new PlayQueue();
            var snap = await persistence.LoadAsync();
            persistence.RestoreInto(restored, snap!);

            restored.Count.Should().Be(2);
            restored.Items[0].Track.Title.Should().Be("Song1");
            restored.CurrentIndex.Should().Be(0);

            File.Delete(path);
        }
    }
}
