using FluentAssertions;
using Spectralis.Core.Audio;
using Spectralis.Core.Streaming;
using Xunit;

namespace Spectralis.Tests.Streaming
{
    public class StreamingTrackExtensionsTests
    {
        private static StreamingTrack MakeTrack() => new()
        {
            Id = "abc123",
            Title = "Test Song",
            Artist = "Test Artist",
            Album = "Test Album",
            SourceId = "soundcloud",
            DurationSeconds = 213.5
        };

        [Fact]
        public void ToTrackInfo_MapsAllFields()
        {
            var track = MakeTrack();
            var info = track.ToTrackInfo();

            info.Title.Should().Be("Test Song");
            info.Artist.Should().Be("Test Artist");
            info.Album.Should().Be("Test Album");
            info.DurationSeconds.Should().BeApproximately(213.5, 0.01);
        }

        [Fact]
        public void ToTrackInfo_FilePathIsStreamingUri()
        {
            var track = MakeTrack();
            var info = track.ToTrackInfo();
            info.FilePath.Should().StartWith("streaming://");
            info.FilePath.Should().Contain("soundcloud");
            info.FilePath.Should().Contain("abc123");
        }

        [Fact]
        public void ToTrackInfo_EmptyFields_DoesNotThrow()
        {
            var track = new StreamingTrack();
            var act = () => track.ToTrackInfo();
            act.Should().NotThrow();
        }
    }
}
