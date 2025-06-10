using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Newtonsoft.Json.Linq;

namespace Spectralis.Streaming
{
    public class SpotifySource : IStreamingSource
    {
        public string Name => "Spotify";

        private readonly SpotifyAuthConfig _config;
        private readonly SpotifyTokenManager _tokenManager;
        private readonly HttpClient _http;
        private string _cachedToken;
        private DateTime _tokenExpiry;
        private bool _disposed;

        public bool IsAuthenticated => _tokenManager.HasTokens;

        public SpotifySource(SpotifyAuthConfig config)
        {
            _config = config;
            _tokenManager = new SpotifyTokenManager(config);
            _http = new HttpClient();
        }

        public async Task AuthenticateAsync(CancellationToken ct = default)
        {
            var verifier = PkceHelper.GenerateCodeVerifier();
            var challenge = PkceHelper.GenerateCodeChallenge(verifier);
            var state = PkceHelper.GenerateState();

            var authUrl = $"https://accounts.spotify.com/authorize" +
                $"?response_type=code" +
                $"&client_id={Uri.EscapeDataString(_config.ClientId)}" +
                $"&scope={Uri.EscapeDataString(string.Join(" ", _config.Scopes))}" +
                $"&redirect_uri={Uri.EscapeDataString(_config.RedirectUri)}" +
                $"&state={state}" +
                $"&code_challenge={challenge}" +
                $"&code_challenge_method=S256";

            System.Diagnostics.Process.Start(authUrl);
        }

        private async Task<string> GetTokenAsync(CancellationToken ct)
        {
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
                return _cachedToken;

            var token = await _tokenManager.GetValidAccessTokenAsync(ct);
            _cachedToken = token;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(55);
            return token;
        }

        public async Task<StreamingTrack> SearchAsync(string query, CancellationToken ct = default)
        {
            var token = await GetTokenAsync(ct);
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.spotify.com/v1/search?q={Uri.EscapeDataString(query)}&type=track&limit=1");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _http.SendAsync(req, ct);
            var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
            var items = json["tracks"]["items"] as JArray;
            if (items == null || items.Count == 0) return null;

            var item = items[0];
            return new StreamingTrack
            {
                Id = item["id"].Value<string>(),
                Title = item["name"].Value<string>(),
                Artist = item["artists"][0]["name"].Value<string>(),
                Album = item["album"]["name"].Value<string>(),
                Duration = TimeSpan.FromMilliseconds(item["duration_ms"].Value<long>()),
                ThumbnailUrl = item["album"]["images"]?[0]?["url"]?.Value<string>(),
                Source = "Spotify"
            };
        }

        public Task<WaveStream> OpenStreamAsync(StreamingTrack track, CancellationToken ct = default)
        {
            throw new NotSupportedException("Spotify direct stream requires Web Playback SDK.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http.Dispose();
            _tokenManager.Dispose();
        }
    }
}
