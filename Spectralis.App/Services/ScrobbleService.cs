using System;
using System.Timers;
using Spectralis.Core.Audio;
using Spectralis.Core.Models;
using Spectralis.Core.Scrobbling;

namespace Spectralis.App.Services
{
    public class ScrobbleService : IDisposable
    {
        private readonly IScrobbler _scrobbler;
        private readonly IAudioEngine _engine;
        private TrackInfo? _currentTrack;
        private DateTimeOffset _trackStarted;
        private bool _scrobbled;
        private bool _disposed;

        public ScrobbleService(IScrobbler scrobbler, IAudioEngine engine)
        {
            _scrobbler = scrobbler;
            _engine = engine;

            _engine.TrackLoaded += OnTrackLoaded;
            _engine.PlaybackStopped += OnPlaybackStopped;
        }

        private void OnTrackLoaded(object? sender, TrackInfo track)
        {
            _currentTrack = track;
            _trackStarted = DateTimeOffset.UtcNow;
            _scrobbled = false;

            if (_scrobbler.IsAuthenticated)
                _ = _scrobbler.NowPlayingAsync(track);
        }

        private void OnPlaybackStopped(object? sender, EventArgs e)
        {
            if (_currentTrack == null || _scrobbled) return;

            double elapsed = (DateTimeOffset.UtcNow - _trackStarted).TotalSeconds;
            double threshold = Math.Min(240, _currentTrack.Duration.TotalSeconds * 0.5);

            if (elapsed >= threshold && elapsed >= 30 && _scrobbler.IsAuthenticated)
            {
                _scrobbled = true;
                _ = _scrobbler.ScrobbleAsync(new ScrobbleEntry
                {
                    Track = _currentTrack,
                    Timestamp = _trackStarted,
                    DurationSeconds = (int)_currentTrack.Duration.TotalSeconds
                });
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _engine.TrackLoaded -= OnTrackLoaded;
            _engine.PlaybackStopped -= OnPlaybackStopped;
        }
    }
}
