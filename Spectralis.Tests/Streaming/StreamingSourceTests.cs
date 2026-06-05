using Spectralis.Core.Streaming;
using Xunit;

namespace Spectralis.Tests.Streaming
{
    public class StreamingSourceTests
    {
        [Fact]
        public void StreamingTrack_HasExpectedFields()
        {
            var track = new StreamingTrack
            {
                Id = "abc123",
                Title = "My Song",
                Artist = "Someone",
                Album = "Album",
                DurationMs = 230000,
                StreamUrl = "https://cdn.example.com/stream/abc123"
            };
            Assert.Equal("abc123", track.Id);
            Assert.Equal(230000, track.DurationMs);
        }

        [Fact]
        public void StreamingTrack_DurationSeconds_ConvertsCorrectly()
        {
            var track = new StreamingTrack { DurationMs = 180000 };
            Assert.Equal(180, track.DurationSeconds);
        }

        [Fact]
        public void StreamingTrack_NullUrl_IsHandled()
        {
            var track = new StreamingTrack { StreamUrl = null };
            Assert.Null(track.StreamUrl);
        }
    }
}
