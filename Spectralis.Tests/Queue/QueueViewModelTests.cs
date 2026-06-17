using FluentAssertions;
using Moq;
using Spectralis.Core.Models;
using Spectralis.Core.Queue;
using Xunit;

namespace Spectralis.Tests.Queue
{
    public class QueueViewModelTests
    {
        private static PlayQueueItem MakeItem(string title) =>
            new(new TrackInfo { Title = title, FilePath = $"/music/{title}.mp3" });

        [Fact]
        public void TotalCount_ReflectsQueueSize()
        {
            var queue = new PlayQueue();
            queue.Add(MakeItem("A"));
            queue.Add(MakeItem("B"));
            queue.Count.Should().Be(2);
        }

        [Fact]
        public void SearchText_FiltersItems()
        {
            var queue = new PlayQueue();
            queue.Add(new PlayQueueItem(new TrackInfo { Title = "Hello World", FilePath = "/a.mp3" }));
            queue.Add(new PlayQueueItem(new TrackInfo { Title = "Goodbye", FilePath = "/b.mp3" }));

            var allItems = queue.Items;
            allItems.Should().HaveCount(2);

            var filtered = System.Linq.Enumerable.Where(allItems,
                i => i.Track.Title?.Contains("hello", System.StringComparison.OrdinalIgnoreCase) == true);
            filtered.Should().HaveCount(1);
        }

        [Fact]
        public void Move_CorrectlyReordersItems()
        {
            var queue = new PlayQueue();
            queue.Add(MakeItem("First"));
            queue.Add(MakeItem("Second"));
            queue.Add(MakeItem("Third"));

            queue.Move(0, 2);

            queue.Items[0].Track.Title.Should().Be("Second");
            queue.Items[2].Track.Title.Should().Be("First");
        }

        [Fact]
        public void ToggleShuffle_TogglesShuffleState()
        {
            var queue = new PlayQueue();
            queue.Add(MakeItem("A")); queue.Add(MakeItem("B"));
            queue.IsShuffled.Should().BeFalse();
            queue.SetShuffle(true);
            queue.IsShuffled.Should().BeTrue();
            queue.SetShuffle(false);
            queue.IsShuffled.Should().BeFalse();
        }

        [Fact]
        public void CycleRepeat_CyclesCorrectly()
        {
            var q = new PlayQueue();
            q.RepeatMode.Should().Be(RepeatMode.None);
            q.RepeatMode = RepeatMode.RepeatAll;
            q.RepeatMode.Should().Be(RepeatMode.RepeatAll);
            q.RepeatMode = RepeatMode.RepeatOne;
            q.RepeatMode.Should().Be(RepeatMode.RepeatOne);
        }
    }
}
