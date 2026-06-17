using Spectralis.App.Services;
using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.SharedPlay
{
    public class OBSOverlayTests
    {
        [Fact]
        public void OBSOverlayState_DefaultFields_AreExpected()
        {
            var state = new OBSOverlayState();
            Assert.Null(state.Title);
            Assert.Null(state.Artist);
            Assert.False(state.IsPlaying);
            Assert.Equal(0.0, state.PositionSeconds);
        }

        [Fact]
        public void OBSOverlayState_CanSetFields()
        {
            var state = new OBSOverlayState
            {
                Title = "My Track",
                Artist = "My Artist",
                Album = "My Album",
                IsPlaying = true,
                PositionSeconds = 42.5,
                DurationSeconds = 180.0
            };
            Assert.Equal("My Track", state.Title);
            Assert.True(state.IsPlaying);
            Assert.Equal(42.5, state.PositionSeconds);
        }

        [Fact]
        public void DefaultPort_Is5128()
        {
            Assert.Equal(5128, OBSOverlayServer.DefaultPort);
        }
    }
}
