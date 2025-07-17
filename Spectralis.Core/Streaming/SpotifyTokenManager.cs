using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Spectralis.Core.Streaming
{
    public class SpotifyTokenSet
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public DateTime IssuedAt { get; set; }
        public bool IsExpired => DateTime.UtcNow >= IssuedAt.AddSeconds(ExpiresIn - 60);
    }

    public class SpotifyTokenManager : IDisposable
    {
        private readonly string _clientId;
        private readonly HttpClient _http;
        private SpotifyTokenSet? _token;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public SpotifyTokenManager(string clientId)
        {
            _clientId = clientId;
            _http = new HttpClient();
        }

        public void SetToken(SpotifyTokenSet token) => _token = token;

        public bool HasToken => _token != null;

        public async Task<string> GetValidAccessTokenAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                if (_token == null) throw new InvalidOperationException("Not authenticated");
                if (!_token.IsExpired) return _token.AccessToken;
                await RefreshAsync(ct);
                return _token!.AccessToken;
            }
            finally { _lock.Release(); }
        }

        private async Task RefreshAsync(CancellationToken ct)
        {
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", _token!.RefreshToken),
                new KeyValuePair<string, string>("client_id", _clientId)
            });

            var resp = await _http.PostAsync("https://accounts.spotify.com/api/token", body, ct);
            resp.EnsureSuccessStatusCode();
            var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

            _token.AccessToken = json.RootElement.GetProperty("access_token").GetString()!;
            _token.ExpiresIn = json.RootElement.GetProperty("expires_in").GetInt32();
            _token.IssuedAt = DateTime.UtcNow;
        }

        public void Dispose() => _http.Dispose();
    }
}
