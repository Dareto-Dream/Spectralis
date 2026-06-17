using System;

namespace Spectralis.Core.AlbumWorld
{
    public class WorldPlayStatsTracker
    {
        private readonly AlbumWorldSession _session;
        private string? _currentTrackId;
        private DateTimeOffset _playStart;

        public WorldPlayStatsTracker(AlbumWorldSession session)
        {
            _session = session;
        }

        public void TrackStarted(string trackId)
        {
            _currentTrackId = trackId;
            _playStart = DateTimeOffset.UtcNow;
        }

        public void TrackStopped()
        {
            if (_currentTrackId == null) return;
            var elapsed = DateTimeOffset.UtcNow - _playStart;
            _session.RecordPlay(_currentTrackId, elapsed);
            _currentTrackId = null;
        }

        public void TrackSkipped()
        {
            TrackStopped();
        }
    }
}
