using System;
using Spectralis.Core.AlbumWorld;
using Xunit;

namespace Spectralis.Tests.AlbumWorld
{
    public class UnlockEvaluatorTests
    {
        private readonly UnlockEvaluator _evaluator = new();

        [Fact]
        public void GetUnlockableTrackIds_UnlocksFreeTrack()
        {
            var manifest = new AlbumWorldManifest();
            manifest.Tracks.Add(new WorldTrack { Id = "t1", IsLocked = true, UnlockAfter = null });
            var session = new AlbumWorldSession();

            var ids = _evaluator.GetUnlockableTrackIds(manifest, session);
            Assert.Contains("t1", ids);
        }

        [Fact]
        public void GetUnlockableTrackIds_DoesNotUnlockWhenPrerequisiteNotPlayed()
        {
            var manifest = new AlbumWorldManifest();
            manifest.Tracks.Add(new WorldTrack { Id = "t2", IsLocked = true, UnlockAfter = "t1" });
            var session = new AlbumWorldSession();

            var ids = _evaluator.GetUnlockableTrackIds(manifest, session);
            Assert.DoesNotContain("t2", ids);
        }

        [Fact]
        public void GetUnlockableTrackIds_UnlocksWhenPrerequisitePlayed()
        {
            var manifest = new AlbumWorldManifest();
            manifest.Tracks.Add(new WorldTrack { Id = "t2", IsLocked = true, UnlockAfter = "t1" });
            var session = new AlbumWorldSession();
            session.RecordPlay("t1", TimeSpan.FromSeconds(200));

            var ids = _evaluator.GetUnlockableTrackIds(manifest, session);
            Assert.Contains("t2", ids);
        }

        [Fact]
        public void ApplyUnlocks_SetsUnlockedOnSession()
        {
            var manifest = new AlbumWorldManifest();
            manifest.Tracks.Add(new WorldTrack { Id = "t1", IsLocked = true });
            var session = new AlbumWorldSession();

            _evaluator.ApplyUnlocks(manifest, session);

            Assert.True(session.Stats["t1"].Unlocked);
        }
    }
}
