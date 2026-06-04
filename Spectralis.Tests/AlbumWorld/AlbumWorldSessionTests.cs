using System;
using Spectralis.Core.AlbumWorld;
using Xunit;

namespace Spectralis.Tests.AlbumWorld
{
    public class AlbumWorldSessionTests
    {
        [Fact]
        public void RecordPlay_IncrementsPlayCount()
        {
            var session = new AlbumWorldSession();
            session.RecordPlay("track-1", TimeSpan.FromSeconds(180));
            session.RecordPlay("track-1", TimeSpan.FromSeconds(180));
            Assert.Equal(2, session.GetPlayCount("track-1"));
        }

        [Fact]
        public void RecordPlay_AccumulatesTotalListenTime()
        {
            var session = new AlbumWorldSession();
            session.RecordPlay("track-1", TimeSpan.FromSeconds(120));
            session.RecordPlay("track-1", TimeSpan.FromSeconds(60));
            Assert.Equal(TimeSpan.FromSeconds(180), session.GetTotalListenTime("track-1"));
        }

        [Fact]
        public void GetPlayCount_UnknownTrack_ReturnsZero()
        {
            var session = new AlbumWorldSession();
            Assert.Equal(0, session.GetPlayCount("nonexistent"));
        }

        [Fact]
        public void RecordPlay_MultipleTracksTrackedIndependently()
        {
            var session = new AlbumWorldSession();
            session.RecordPlay("track-1", TimeSpan.FromSeconds(200));
            session.RecordPlay("track-2", TimeSpan.FromSeconds(150));
            session.RecordPlay("track-2", TimeSpan.FromSeconds(150));
            Assert.Equal(1, session.GetPlayCount("track-1"));
            Assert.Equal(2, session.GetPlayCount("track-2"));
        }
    }
}
