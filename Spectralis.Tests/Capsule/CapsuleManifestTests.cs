using System;
using System.Collections.Generic;
using Spectralis.Core.Capsule;
using Xunit;

namespace Spectralis.Tests.Capsule
{
    public class CapsuleManifestTests
    {
        [Fact]
        public void CapsuleManifest_DefaultVersion_Is50()
        {
            var manifest = new CapsuleManifest();
            Assert.Equal("5.0", manifest.Version);
        }

        [Fact]
        public void CapsuleManifest_Tracks_DefaultEmpty()
        {
            var manifest = new CapsuleManifest();
            Assert.NotNull(manifest.Tracks);
            Assert.Empty(manifest.Tracks);
        }

        [Fact]
        public void CapsuleManifest_Meta_DefaultEmpty()
        {
            var manifest = new CapsuleManifest();
            Assert.NotNull(manifest.Meta);
            Assert.Empty(manifest.Meta);
        }

        [Fact]
        public void CapsuleTrack_HasExpectedFields()
        {
            var track = new CapsuleTrack
            {
                Id = "track-1",
                Title = "Intro",
                AudioFile = "intro.mp3",
                Order = 1
            };
            Assert.Equal("track-1", track.Id);
            Assert.Equal(1, track.Order);
        }

        [Fact]
        public void CapsuleTrust_IsVerifiedDefault_IsFalse()
        {
            var trust = new CapsuleTrust();
            Assert.False(trust.IsVerified);
        }

        [Fact]
        public void CapsuleManifest_MetaDictionary_CanStoreValues()
        {
            var manifest = new CapsuleManifest();
            manifest.Meta["genre"] = "Electronic";
            manifest.Meta["year"] = "2026";
            Assert.Equal("Electronic", manifest.Meta["genre"]);
        }
    }
}
