using System.Collections.Generic;
using Spectralis.Core.Library;
using Spectralis.Core.Playlists;
using Xunit;

namespace Spectralis.Tests.Playlist
{
    public class PlaylistTests
    {
        [Fact]
        public void StaticPlaylist_AddTrack_IncreasesCount()
        {
            var pl = new StaticPlaylist { Name = "Faves" };
            pl.TrackIds.Add(1);
            pl.TrackIds.Add(2);
            Assert.Equal(2, pl.TrackIds.Count);
        }

        [Fact]
        public void StaticPlaylist_RemoveTrack_DecreasesCount()
        {
            var pl = new StaticPlaylist { Name = "Test" };
            pl.TrackIds.Add(10);
            pl.TrackIds.Add(20);
            pl.TrackIds.Remove(10);
            Assert.Single(pl.TrackIds);
        }

        [Fact]
        public void SmartPlaylist_HasRules()
        {
            var pl = new SmartPlaylist
            {
                Name = "Recent Rock",
                Rules = new List<SmartPlaylistRule>
                {
                    new SmartPlaylistRule { Field = "genre", Operator = "equals", Value = "Rock" },
                    new SmartPlaylistRule { Field = "year", Operator = "greater_than", Value = "2018" }
                }
            };
            Assert.Equal(2, pl.Rules.Count);
        }

        [Fact]
        public void StaticPlaylist_Name_Persists()
        {
            var pl = new StaticPlaylist { Name = "My Playlist" };
            Assert.Equal("My Playlist", pl.Name);
        }
    }
}
