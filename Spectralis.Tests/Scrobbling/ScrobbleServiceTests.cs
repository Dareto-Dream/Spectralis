using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Spectralis.Core.Scrobbling;
using Xunit;

namespace Spectralis.Tests.Scrobbling
{
    public class ScrobbleServiceTests
    {
        [Fact]
        public async Task Scrobble_WithValidTrack_CallsAllScrobblers()
        {
            var mock1 = new Mock<IScrobbler>();
            var mock2 = new Mock<IScrobbler>();
            mock1.Setup(s => s.IsEnabled).Returns(true);
            mock2.Setup(s => s.IsEnabled).Returns(true);

            var service = new ScrobbleService(new[] { mock1.Object, mock2.Object });
            var track = new ScrobbleTrack { Title = "Song", Artist = "Artist", Album = "Album", DurationSeconds = 240 };
            await service.ScrobbleAsync(track);

            mock1.Verify(s => s.ScrobbleAsync(track), Times.Once);
            mock2.Verify(s => s.ScrobbleAsync(track), Times.Once);
        }

        [Fact]
        public async Task Scrobble_DisabledScrobbler_IsSkipped()
        {
            var enabled = new Mock<IScrobbler>();
            var disabled = new Mock<IScrobbler>();
            enabled.Setup(s => s.IsEnabled).Returns(true);
            disabled.Setup(s => s.IsEnabled).Returns(false);

            var service = new ScrobbleService(new[] { enabled.Object, disabled.Object });
            var track = new ScrobbleTrack { Title = "Song", Artist = "Artist", DurationSeconds = 200 };
            await service.ScrobbleAsync(track);

            enabled.Verify(s => s.ScrobbleAsync(It.IsAny<ScrobbleTrack>()), Times.Once);
            disabled.Verify(s => s.ScrobbleAsync(It.IsAny<ScrobbleTrack>()), Times.Never);
        }

        [Fact]
        public async Task Scrobble_ShortTrack_IsSkipped()
        {
            var mock = new Mock<IScrobbler>();
            mock.Setup(s => s.IsEnabled).Returns(true);
            var service = new ScrobbleService(new[] { mock.Object });
            var track = new ScrobbleTrack { Title = "Intro", Artist = "Band", DurationSeconds = 20 };
            await service.ScrobbleAsync(track);
            mock.Verify(s => s.ScrobbleAsync(It.IsAny<ScrobbleTrack>()), Times.Never);
        }

        [Fact]
        public async Task Scrobble_ScrobblerThrows_DoesNotPropagateException()
        {
            var mock = new Mock<IScrobbler>();
            mock.Setup(s => s.IsEnabled).Returns(true);
            mock.Setup(s => s.ScrobbleAsync(It.IsAny<ScrobbleTrack>())).ThrowsAsync(new Exception("network error"));
            var service = new ScrobbleService(new[] { mock.Object });
            var track = new ScrobbleTrack { Title = "Song", Artist = "Artist", DurationSeconds = 240 };
            var ex = await Record.ExceptionAsync(() => service.ScrobbleAsync(track));
            Assert.Null(ex);
        }
    }
}
