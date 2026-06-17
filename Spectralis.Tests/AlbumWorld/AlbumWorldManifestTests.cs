using System.Collections.Generic;
using Spectralis.Core.AlbumWorld;
using Xunit;

namespace Spectralis.Tests.AlbumWorld
{
    public class AlbumWorldManifestTests
    {
        [Fact]
        public void WorldTrack_IsLocked_DefaultFalse()
        {
            var track = new WorldTrack { Id = "t1", Title = "Open Track" };
            Assert.False(track.IsLocked);
        }

        [Fact]
        public void WorldTrack_WithUnlockAfter_IsLocked()
        {
            var track = new WorldTrack
            {
                Id = "t2",
                Title = "Locked Track",
                IsLocked = true,
                UnlockAfter = "t1"
            };
            Assert.True(track.IsLocked);
            Assert.Equal("t1", track.UnlockAfter);
        }

        [Fact]
        public void Achievement_Hidden_DefaultFalse()
        {
            var ach = new Achievement { Id = "ach-1", Name = "First Listen" };
            Assert.False(ach.IsHidden);
        }

        [Fact]
        public void AlbumWorldManifest_Tracks_DefaultEmpty()
        {
            var manifest = new AlbumWorldManifest();
            Assert.NotNull(manifest.Tracks);
        }

        [Fact]
        public void AlbumWorldManifest_Achievements_DefaultEmpty()
        {
            var manifest = new AlbumWorldManifest();
            Assert.NotNull(manifest.Achievements);
        }

        [Fact]
        public void Achievement_HiddenCondition_CanBeSet()
        {
            var ach = new Achievement
            {
                Id = "secret",
                Name = "Secret Achievement",
                IsHidden = true,
                Condition = "play_count:10"
            };
            Assert.True(ach.IsHidden);
            Assert.Equal("play_count:10", ach.Condition);
        }
    }
}
