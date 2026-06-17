using FluentAssertions;
using Spectralis.Core.Models;
using Spectralis.Core.Queue;
using Xunit;

namespace Spectralis.Tests.Queue
{
    public class PlayQueueTests
    {
        private static PlayQueueItem MakeItem(string title) =>
            new(new TrackInfo { Title = title, FilePath = $"/music/{title}.mp3" });

        [Fact]
        public void Add_IncreasesCount()
        {
            var q = new PlayQueue();
            q.Add(MakeItem("A"));
            q.Count.Should().Be(1);
        }

        [Fact]
        public void PlayAt_SetsCurrentIndex()
        {
            var q = new PlayQueue();
            q.Add(MakeItem("A")); q.Add(MakeItem("B"));
            q.PlayAt(1);
            q.CurrentIndex.Should().Be(1);
            q.Current!.Track.Title.Should().Be("B");
        }

        [Fact]
        public void Next_AdvancesLinear()
        {
            var q = new PlayQueue();
            q.Add(MakeItem("A")); q.Add(MakeItem("B")); q.Add(MakeItem("C"));
            q.PlayAt(0);
            q.Next()!.Track.Title.Should().Be("B");
            q.Next()!.Track.Title.Should().Be("C");
            q.Next().Should().BeNull();
        }

        [Fact]
        public void Move_UpdatesItemOrder()
        {
            var q = new PlayQueue();
            q.Add(MakeItem("A")); q.Add(MakeItem("B")); q.Add(MakeItem("C"));
            q.Move(0, 2);
            q.Items[0].Track.Title.Should().Be("B");
            q.Items[1].Track.Title.Should().Be("C");
            q.Items[2].Track.Title.Should().Be("A");
        }

        [Fact]
        public void Move_UpdatesCurrentIndex_WhenCurrentMoved()
        {
            var q = new PlayQueue();
            q.Add(MakeItem("A")); q.Add(MakeItem("B")); q.Add(MakeItem("C"));
            q.PlayAt(0);
            q.Move(0, 2);
            q.CurrentIndex.Should().Be(2);
        }

        [Fact]
        public void Move_WithShuffle_UpdatesShuffleOrderIndices()
        {
            var q = new PlayQueue();
            q.Add(MakeItem("A")); q.Add(MakeItem("B")); q.Add(MakeItem("C"));
            q.SetShuffle(true);
            q.PlayAt(0);
            q.Move(0, 2);
            var next = q.Next();
            next.Should().NotBeNull();
            q.Items.Should().HaveCount(3);
        }

        [Fact]
        public void Shuffle_Next_DoesNotSkipTracks()
        {
            var q = new PlayQueue();
            for (int i = 0; i < 5; i++) q.Add(MakeItem($"Track{i}"));
            q.SetShuffle(true);
            q.PlayAt(0);

            var seen = new System.Collections.Generic.HashSet<string>();
            seen.Add(q.Current!.Track.Title);
            for (int i = 0; i < 4; i++)
            {
                var n = q.Next();
                n.Should().NotBeNull();
                seen.Add(n!.Track.Title);
            }
            seen.Should().HaveCount(5);
        }

        [Fact]
        public void RepeatOne_Next_ReturnsCurrentTrack()
        {
            var q = new PlayQueue();
            q.Add(MakeItem("A")); q.Add(MakeItem("B"));
            q.PlayAt(0);
            q.RepeatMode = RepeatMode.RepeatOne;
            q.Next()!.Track.Title.Should().Be("A");
        }

        [Fact]
        public void Clear_ResetsQueue()
        {
            var q = new PlayQueue();
            q.Add(MakeItem("A")); q.Add(MakeItem("B"));
            q.PlayAt(0);
            q.Clear();
            q.Count.Should().Be(0);
            q.Current.Should().BeNull();
        }

        [Fact]
        public void Remove_FixesCurrentIndex()
        {
            var q = new PlayQueue();
            q.Add(MakeItem("A")); q.Add(MakeItem("B")); q.Add(MakeItem("C"));
            q.PlayAt(2);
            q.Remove(q.Items[0]);
            q.CurrentIndex.Should().Be(1);
            q.Current!.Track.Title.Should().Be("C");
        }
    }
}
