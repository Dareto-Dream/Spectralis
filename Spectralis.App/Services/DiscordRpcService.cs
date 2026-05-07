using System;

namespace Spectralis.App.Services
{
    public class DiscordRpcService : IDisposable
    {
        private bool _initialized;
        private readonly string _clientId;

        public bool IsEnabled { get; set; } = true;

        public DiscordRpcService(string clientId)
        {
            _clientId = clientId;
        }

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
        }

        public void UpdatePresence(DiscordPresence presence)
        {
            if (!IsEnabled || !_initialized) return;
        }

        public void ClearPresence()
        {
            if (!_initialized) return;
        }

        public void Dispose()
        {
            if (_initialized)
                ClearPresence();
            _initialized = false;
        }
    }

    public class DiscordPresence
    {
        public string Details { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string? LargeImageKey { get; set; }
        public string? SmallImageKey { get; set; }
        public DateTimeOffset? StartTimestamp { get; set; }
        public DateTimeOffset? EndTimestamp { get; set; }
    }
}
