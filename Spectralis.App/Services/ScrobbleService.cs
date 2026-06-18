using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Spectralis.App.ViewModels;
using Spectralis.Core.Audio;
using Spectralis.Core.Scrobbling;

namespace Spectralis.App.Services
{
    public class ScrobbleService : IDisposable
    {
        private readonly List<IScrobbler> _scrobblers;
        private readonly IAudioEngine _engine;
        private readonly string _offlineQueuePath;
        private TrackInfo? _currentTrack;
        private DateTimeOffset _trackStarted;
        private bool _scrobbled;
        private bool _disposed;

        public bool IsEnabled { get; set; } = true;
        public int PendingQueueCount { get; private set; }
        public event EventHandler<ScrobbleStatusEventArgs>? StatusChanged;

        public ScrobbleService(IEnumerable<IScrobbler> scrobblers, IAudioEngine engine, string offlineQueuePath)
        {
            _scrobblers = scrobblers.ToList();
            _engine = engine;
            _offlineQueuePath = offlineQueuePath;

            _engine.TrackLoaded += OnTrackLoaded;
            _engine.PlaybackStopped += OnPlaybackStopped;

            _ = FlushPendingAsync();
        }

        private void OnTrackLoaded(object? sender, TrackInfo track)
        {
            _currentTrack = track;
            _trackStarted = DateTimeOffset.UtcNow;
            _scrobbled = false;
            if (!IsEnabled) return;
            foreach (var s in _scrobblers)
                if (s.IsConfigured) _ = s.NowPlayingAsync(track);
        }

        private void OnPlaybackStopped(object? sender, EventArgs e)
        {
            if (!IsEnabled || _currentTrack == null || _scrobbled) return;

            double elapsed = (DateTimeOffset.UtcNow - _trackStarted).TotalSeconds;
            double duration = _currentTrack.DurationSeconds;
            double threshold = Math.Min(240.0, duration * 0.5);

            if (elapsed >= threshold && elapsed >= 30)
            {
                _scrobbled = true;
                _ = SubmitScrobbleAsync(_currentTrack, _trackStarted.UtcDateTime);
            }
        }

        private async Task SubmitScrobbleAsync(TrackInfo track, DateTime playedAt)
        {
            bool anyFailed = false;
            foreach (var s in _scrobblers)
            {
                if (!s.IsConfigured) continue;
                try { await s.ScrobbleAsync(track, playedAt); }
                catch { anyFailed = true; }
            }

            if (anyFailed) await AppendToOfflineQueueAsync(track, playedAt);
            RaiseStatus();
        }

        public async Task FlushPendingAsync()
        {
            if (!File.Exists(_offlineQueuePath)) return;
            try
            {
                var json = await File.ReadAllTextAsync(_offlineQueuePath);
                var entries = JsonSerializer.Deserialize<List<OfflineEntry>>(json) ?? new();
                var remaining = new List<OfflineEntry>();

                foreach (var entry in entries)
                {
                    bool ok = true;
                    foreach (var s in _scrobblers.Where(s => s.IsConfigured))
                    {
                        try { await s.ScrobbleAsync(entry.Track, entry.PlayedAt); }
                        catch { ok = false; }
                    }
                    if (!ok) remaining.Add(entry);
                }

                PendingQueueCount = remaining.Count;
                await File.WriteAllTextAsync(_offlineQueuePath, JsonSerializer.Serialize(remaining));
                RaiseStatus();
            }
            catch { }
        }

        private async Task AppendToOfflineQueueAsync(TrackInfo track, DateTime playedAt)
        {
            var entries = new List<OfflineEntry>();
            if (File.Exists(_offlineQueuePath))
            {
                try
                {
                    entries = JsonSerializer.Deserialize<List<OfflineEntry>>(
                        await File.ReadAllTextAsync(_offlineQueuePath)) ?? new();
                }
                catch { }
            }
            entries.Add(new OfflineEntry { Track = track, PlayedAt = playedAt });
            PendingQueueCount = entries.Count;
            await File.WriteAllTextAsync(_offlineQueuePath, JsonSerializer.Serialize(entries));
        }

        private void RaiseStatus()
        {
            StatusChanged?.Invoke(this, new ScrobbleStatusEventArgs
            {
                LastFmConnected = _scrobblers.OfType<LastFmScrobbler>().Any(s => s.IsConfigured),
                ListenBrainzConnected = _scrobblers.OfType<ListenBrainzScrobbler>().Any(s => s.IsConfigured),
                PendingCount = PendingQueueCount
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _engine.TrackLoaded -= OnTrackLoaded;
            _engine.PlaybackStopped -= OnPlaybackStopped;
        }

        private sealed class OfflineEntry
        {
            public TrackInfo Track { get; set; } = new();
            public DateTime PlayedAt { get; set; }
        }
    }
}
