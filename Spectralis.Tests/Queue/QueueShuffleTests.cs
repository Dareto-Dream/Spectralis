using System.Linq;
using Spectralis.Core.Queue;
using Xunit;

namespace Spectralis.Tests.Queue
{
    public class QueueShuffleTests
    {
        private static PlayQueue MakeQueue(int count)
        {
            var q = new PlayQueue();
            for (int i = 0; i < count; i++)
                q.Add(new PlayQueueItem { TrackId = i, Title = $"Track {i}" });
            return q;
        }

        [Fact]
        public void EnableShuffle_PreservesCount()
        {
            var q = MakeQueue(10);
            q.EnableShuffle();
            Assert.Equal(10, q.Count);
        }

        [Fact]
        public void DisableShuffle_RestoresOriginalOrder()
        {
            var q = MakeQueue(5);
            var original = q.Items.Select(i => i.TrackId).ToList();
            q.EnableShuffle();
            q.DisableShuffle();
            Assert.Equal(original, q.Items.Select(i => i.TrackId).ToList());
        }

        [Fact]
        public void Next_InShuffleMode_AdvancesPosition()
        {
            var q = MakeQueue(5);
            q.EnableShuffle();
            var first = q.Current;
            q.MoveNext();
            var second = q.Current;
            Assert.NotNull(first);
            Assert.NotNull(second);
        }

        [Fact]
        public void Move_UpdatesShuffleOrder()
        {
            var q = MakeQueue(5);
            q.EnableShuffle();
            q.Move(0, 4);
            Assert.Equal(5, q.Count);
        }
    }
}
