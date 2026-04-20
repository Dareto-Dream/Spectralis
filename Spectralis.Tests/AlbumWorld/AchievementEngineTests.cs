using System;
using Spectralis.Core.AlbumWorld;
using Xunit;

namespace Spectralis.Tests.AlbumWorld
{
    public class AchievementEngineTests
    {
        private readonly AchievementEngine _engine = new();

        private static AlbumWorldManifest BuildManifest(params Achievement[] achievements)
        {
            var m = new AlbumWorldManifest();
            foreach (var a in achievements) m.Achievements.Add(a);
            return m;
        }

        [Fact]
        public void Evaluate_PlayCount_EarnsWhenMet()
        {
            var manifest = BuildManifest(new Achievement
            {
                Id = "play10",
                Condition = "play_count:10"
            });
            var session = new AlbumWorldSession();
            session.RecordPlay("t1", TimeSpan.FromSeconds(200));
            for (int i = 1; i < 10; i++) session.RecordPlay("t1", TimeSpan.FromSeconds(200));

            var earned = _engine.Evaluate(manifest, session);
            Assert.Contains("play10", earned);
        }

        [Fact]
        public void Evaluate_PlayCount_DoesNotEarnWhenNotMet()
        {
            var manifest = BuildManifest(new Achievement { Id = "play100", Condition = "play_count:100" });
            var session = new AlbumWorldSession();
            session.RecordPlay("t1", TimeSpan.FromSeconds(10));

            var earned = _engine.Evaluate(manifest, session);
            Assert.DoesNotContain("play100", earned);
        }

        [Fact]
        public void Evaluate_AlreadyEarned_NotReturned()
        {
            var manifest = BuildManifest(new Achievement { Id = "play1", Condition = "play_count:1" });
            var session = new AlbumWorldSession();
            session.RecordPlay("t1", TimeSpan.FromSeconds(10));
            session.EarnAchievement("play1");

            var earned = _engine.Evaluate(manifest, session);
            Assert.DoesNotContain("play1", earned);
        }

        [Fact]
        public void Evaluate_TrackPlays_EarnsCorrectly()
        {
            var manifest = BuildManifest(new Achievement { Id = "looper", Condition = "track_plays:t1:5" });
            var session = new AlbumWorldSession();
            for (int i = 0; i < 5; i++) session.RecordPlay("t1", TimeSpan.FromSeconds(200));

            var earned = _engine.Evaluate(manifest, session);
            Assert.Contains("looper", earned);
        }
    }
}
