using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Spectralis.Streaming
{
    public class SpotifyRelatedTracks : IDisposable
    {
        private readonly SpotifyTokenManager _tokens;
        private readonly HttpClient _http;

        public SpotifyRelatedTracks(SpotifyTokenManager tokens)
        {
            _tokens = tokens;
            _http = new HttpClient();
        }

        public async Task<List<StreamingTrack>> GetRecommendationsAsync(string seedTrackId, int limit = 10, CancellationToken ct = default)
        {
            string token = await _tokens.GetValidAccessTokenAsync(ct);
            string url = $"https://api.spotify.com/v1/recommendations?seed_tracks={seedTrackId}&limit={limit}";

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return new List<StreamingTrack>();

            var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
            var result = new List<StreamingTrack>();

            foreach (var track in json["tracks"] ?? new JArray())
            {
                result.Add(new StreamingTrack
                {
                    Id = track["id"]?.Value<string>(),
                    Title = track["name"]?.Value<string>(),
                    Artist = track["artists"]?[0]?["name"]?.Value<string>(),
                    Album = track["album"]?["name"]?.Value<string>(),
                    Duration = TimeSpan.FromMilliseconds(track["duration_ms"]?.Value<long>() ?? 0),
                    ThumbnailUrl = track["album"]?["images"]?[0]?["url"]?.Value<string>(),
                    Source = "Spotify"
                });
            }

            return result;
        }

        public void Dispose() => _http.Dispose();
    }
}
