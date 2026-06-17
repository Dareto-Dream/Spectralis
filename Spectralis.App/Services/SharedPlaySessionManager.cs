using System;
using System.Threading.Tasks;
using Spectralis.Core.SharedPlay;

namespace Spectralis.App.Services
{
    public class SharedPlaySessionManager : IDisposable
    {
        private readonly SessionHistoryStore _history;
        private SharedPlayStats? _currentStats;

        public SharedPlayStats? CurrentStats => _currentStats;

        public SharedPlaySessionManager(SessionHistoryStore history)
        {
            _history = history;
        }

        public void StartSession(string roomCode, string sessionId)
        {
            _currentStats = new SharedPlayStats
            {
                SessionId = sessionId,
                RoomCode = roomCode,
                StartedAt = DateTimeOffset.UtcNow
            };
        }

        public void RecordJoin() => _currentStats?.RecordJoin();
        public void UpdatePeak(int count) => _currentStats?.UpdatePeak(count);
        public void RecordTrack(string trackId) => _currentStats?.RecordTrack(trackId);

        public async Task EndSessionAsync()
        {
            if (_currentStats == null) return;
            _currentStats.EndedAt = DateTimeOffset.UtcNow;
            await _history.AppendAsync(_currentStats);
            _currentStats = null;
        }

        public void Dispose() { }
    }
}
