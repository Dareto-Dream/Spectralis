using System.Collections.Generic;

namespace Spectralis.Core.AlbumWorld
{
    public class UnlockEvaluator
    {
        public IReadOnlyList<string> GetUnlockableTrackIds(AlbumWorldManifest manifest, AlbumWorldSession session)
        {
            var result = new List<string>();
            foreach (var track in manifest.Tracks)
            {
                if (!track.IsLocked) continue;
                if (session.Stats.TryGetValue(track.Id, out var s) && s.Unlocked) continue;

                if (track.UnlockAfter == null || session.HasPlayed(track.UnlockAfter))
                    result.Add(track.Id);
            }
            return result;
        }

        public void ApplyUnlocks(AlbumWorldManifest manifest, AlbumWorldSession session)
        {
            foreach (var id in GetUnlockableTrackIds(manifest, session))
                session.UnlockTrack(id);
        }
    }
}
