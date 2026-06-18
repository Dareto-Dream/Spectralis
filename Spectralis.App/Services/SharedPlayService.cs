using System;
using System.Threading.Tasks;
using Spectralis.Core.SharedPlay;

namespace Spectralis.App.Services
{
    public class SharedPlayService : IDisposable
    {
        private readonly SharedPlayClient _client;
        private Func<TimeSpan>? _positionGetter;
        private System.Timers.Timer? _syncTimer;
        private string? _currentTrackId;

        public event EventHandler<SharedPlaySyncPacket>? RemoteSyncReceived;
        public event EventHandler? SessionEnded;

        public bool IsConnected => _client.IsConnected;
        public string? SessionId { get; private set; }

        public SharedPlayService(string serverUri)
        {
            _client = new SharedPlayClient(serverUri);
            _client.SyncReceived += (_, p) => RemoteSyncReceived?.Invoke(this, p);
            _client.SessionEnded += (_, _) => SessionEnded?.Invoke(this, EventArgs.Empty);
            _client.Disconnected += (_, _) => OnDisconnected();
        }

        public async Task HostAsync(string roomCode, string userId, Func<TimeSpan> positionGetter)
        {
            _positionGetter = positionGetter;
            await _client.ConnectAsync(roomCode, userId);
            SessionId = roomCode;
            StartSyncTimer();
        }

        public async Task JoinAsync(string roomCode, string userId)
        {
            await _client.ConnectAsync(roomCode, userId);
            SessionId = roomCode;
        }

        public void SetCurrentTrack(string trackId) => _currentTrackId = trackId;

        private void StartSyncTimer()
        {
            _syncTimer = new System.Timers.Timer(500);
            _syncTimer.Elapsed += async (_, _) => await BroadcastSyncAsync();
            _syncTimer.Start();
        }

        private async Task BroadcastSyncAsync()
        {
            if (!IsConnected || _positionGetter == null) return;
            await _client.SendSyncAsync(new SharedPlaySyncPacket
            {
                SessionId = SessionId ?? string.Empty,
                TrackId = _currentTrackId,
                PositionSeconds = _positionGetter().TotalSeconds,
                IsPlaying = true,
                ServerTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Event = "sync"
            });
        }

        private void OnDisconnected()
        {
            _syncTimer?.Stop();
        }

        public void Dispose()
        {
            _syncTimer?.Stop();
            _syncTimer?.Dispose();
            _client.Dispose();
        }
    }
}
