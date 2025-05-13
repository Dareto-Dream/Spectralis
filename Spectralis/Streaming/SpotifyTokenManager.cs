using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Spectralis.Streaming
{
    public class SpotifyTokenManager : IDisposable
    {
        private readonly SpotifyAuthConfig _config;
        private readonly HttpClient _http;
        private SpotifyTokenSet _tokens;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public SpotifyTokenSet Tokens => _tokens;
        public bool HasTokens => _tokens != null;

        public SpotifyTokenManager(SpotifyAuthConfig config)
        {
            _config = config;
            _http = new HttpClient();
        }

        public void SetTokens(SpotifyTokenSet tokens)
        {
            _tokens = tokens;
        }

        public async Task<string> GetValidAccessTokenAsync(CancellationToken ct = default)
        {
            if (_tokens == null)
                throw new InvalidOperationException("Not authenticated with Spotify.");

            if (!_tokens.IsExpired)
                return _tokens.AccessToken;

            await RefreshAsync(ct);
            return _tokens.AccessToken;
        }

        public async Task RefreshAsync(CancellationToken ct = default)
        {
            await _refreshLock.WaitAsync(ct);
            try
            {
                if (!_tokens.IsExpired) return;

                var body = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = _tokens.RefreshToken,
                    ["client_id"] = _config.ClientId
                });

                var response = await _http.PostAsync("https://accounts.spotify.com/api/token", body, ct);
                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);

                _tokens = new SpotifyTokenSet
                {
                    AccessToken = obj["access_token"].Value<string>(),
                    RefreshToken = obj.ContainsKey("refresh_token")
                        ? obj["refresh_token"].Value<string>()
                        : _tokens.RefreshToken,
                    ExpiresIn = obj["expires_in"].Value<int>(),
                    IssuedAt = DateTime.UtcNow
                };
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http.Dispose();
            _refreshLock.Dispose();
        }
    }
}
