using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Spectralis.Core.Streaming
{
    public class SpotifySource : IStreamingSource
    {
        private readonly SpotifyTokenManager _tokens;
        private readonly HttpClient _http;
        private readonly SemaphoreSlim _refreshGuard = new SemaphoreSlim(1, 1);

        public string Name => "Spotify";
        public bool IsAuthenticated => _tokens.HasToken;

        public SpotifySource(SpotifyTokenManager tokens)
        {
            _tokens = tokens;
            _http = new HttpClient();
        }

        private async Task<string> AcquireTokenAsync(CancellationToken ct)
        {
            await _refreshGuard.WaitAsync(ct);
            try { return await _tokens.GetValidAccessTokenAsync(ct); }
            finally { _refreshGuard.Release(); }
        }

        public async Task<IReadOnlyList<StreamingTrack>> SearchAsync(string query, int limit = 20, CancellationToken ct = default)
        {
            string token = await AcquireTokenAsync(ct);
            string encoded = Uri.EscapeDataString(query);
            string url = $"https://api.spotify.com/v1/search?q={encoded}&type=track&limit={limit}";

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var items = doc.RootElement.GetProperty("tracks").GetProperty("items");
            var result = new List<StreamingTrack>();

            foreach (var item in items.EnumerateArray())
            {
                string? thumbnail = null;
                if (item.TryGetProperty("album", out var album) &&
                    album.TryGetProperty("images", out var images) &&
                    images.GetArrayLength() > 0)
                    thumbnail = images[0].GetProperty("url").GetString();

                string? artist = null;
                if (item.TryGetProperty("artists", out var artists) && artists.GetArrayLength() > 0)
                    artist = artists[0].GetProperty("name").GetString();

                long durMs = item.TryGetProperty("duration_ms", out var d) ? d.GetInt64() : 0;

                result.Add(new StreamingTrack(
                    Id: item.GetProperty("id").GetString() ?? "",
                    Title: item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Artist: artist ?? "",
                    Album: item.TryGetProperty("album", out var alb) && alb.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "",
                    Duration: TimeSpan.FromMilliseconds(durMs),
                    ThumbnailUrl: thumbnail,
                    Source: Name
                ));
            }

            return result;
        }

        public Task<Stream> OpenStreamAsync(StreamingTrack track, CancellationToken ct = default)
        {
            throw new NotSupportedException("Spotify direct stream requires Web Playback SDK — not available in Core");
        }

        public Task<bool> AuthenticateAsync(CancellationToken ct = default)
        {
            throw new NotSupportedException("Authentication must be driven by the App layer (PKCE browser flow)");
        }

        public void Dispose()
        {
            _http.Dispose();
            _refreshGuard.Dispose();
        }
    }
}
