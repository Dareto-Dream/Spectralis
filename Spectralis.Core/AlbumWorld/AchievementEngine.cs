using System.Collections.Generic;

namespace Spectralis.Core.AlbumWorld
{
    public class AchievementEngine
    {
        public IReadOnlyList<string> Evaluate(AlbumWorldManifest manifest, AlbumWorldSession session)
        {
            var earned = new List<string>();
            foreach (var achievement in manifest.Achievements)
            {
                if (session.EarnedAchievements.Contains(achievement.Id)) continue;
                if (MeetsCondition(achievement, session))
                    earned.Add(achievement.Id);
            }
            return earned;
        }

        private bool MeetsCondition(Achievement achievement, AlbumWorldSession session)
        {
            if (achievement.Condition == null) return false;

            var parts = achievement.Condition.Split(':');
            if (parts.Length < 2) return false;

            return parts[0] switch
            {
                "play_count" => int.TryParse(parts[1], out int target) &&
                                CountTotalPlays(session) >= target,
                "all_tracks" => AllTracksPlayed(session, parts.Length > 2 ? parts[2] : null),
                "track_plays" => parts.Length >= 3 &&
                                 int.TryParse(parts[2], out int tgt) &&
                                 session.Stats.TryGetValue(parts[1], out var s) &&
                                 s.PlayCount >= tgt,
                _ => false
            };
        }

        private static int CountTotalPlays(AlbumWorldSession session)
        {
            int total = 0;
            foreach (var s in session.Stats.Values) total += s.PlayCount;
            return total;
        }

        private static bool AllTracksPlayed(AlbumWorldSession session, string? exclude)
        {
            foreach (var kv in session.Stats)
            {
                if (kv.Key == exclude) continue;
                if (kv.Value.PlayCount == 0) return false;
            }
            return session.Stats.Count > 0;
        }
    }
}
