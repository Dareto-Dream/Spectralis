using System;
using Spectralis.App.Services;
using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.SharedPlay
{
    public class ReactionTrackerTests
    {
        private readonly ReactionTracker _tracker = new();

        [Fact]
        public void Record_AddsToHistory()
        {
            _tracker.Record(new SharedPlayReactionMessage { Emoji = "🔥", UserId = "u1" });
            Assert.Single(_tracker.GetHistory());
        }

        [Fact]
        public void GetEmojiCounts_CountsCorrectly()
        {
            _tracker.Record(new SharedPlayReactionMessage { Emoji = "❤️", UserId = "u1" });
            _tracker.Record(new SharedPlayReactionMessage { Emoji = "❤️", UserId = "u2" });
            _tracker.Record(new SharedPlayReactionMessage { Emoji = "🔥", UserId = "u3" });

            var counts = _tracker.GetEmojiCounts();
            Assert.Equal(2, counts["❤️"]);
            Assert.Equal(1, counts["🔥"]);
        }

        [Fact]
        public void ReactionReceived_EventFires()
        {
            SharedPlayReactionMessage? received = null;
            _tracker.ReactionReceived += (_, r) => received = r;
            _tracker.Record(new SharedPlayReactionMessage { Emoji = "💫", UserId = "u1" });
            Assert.NotNull(received);
            Assert.Equal("💫", received!.Emoji);
        }

        [Fact]
        public void Clear_EmptiesHistory()
        {
            _tracker.Record(new SharedPlayReactionMessage { Emoji = "🎵", UserId = "u1" });
            _tracker.Clear();
            Assert.Empty(_tracker.GetHistory());
        }
    }
}
