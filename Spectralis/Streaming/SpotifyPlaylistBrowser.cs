using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Spectralis.Streaming
{
    public class SpotifyPlaylist
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int TrackCount { get; set; }
        public string ThumbnailUrl { get; set; }
    }

    public class SpotifyPlaylistBrowser : IDisposable
    {
        private readonly SpotifyTokenManager _tokens;
        private readonly HttpClient _http;

        public SpotifyPlaylistBrowser(SpotifyTokenManager tokens)
        {
            _tokens = tokens;
            _http = new HttpClient();
        }

        public async Task<List<SpotifyPlaylist>> GetUserPlaylistsAsync(CancellationToken ct = default)
        {
            string token = await _tokens.GetValidAccessTokenAsync(ct);
            var req = new HttpRequestMessage(HttpMethod.Get,
                "https://api.spotify.com/v1/me/playlists?limit=50");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();

            var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
            var result = new List<SpotifyPlaylist>();

            foreach (var item in json["items"] ?? new JArray())
            {
                result.Add(new SpotifyPlaylist
                {
                    Id = item["id"]?.Value<string>(),
                    Name = item["name"]?.Value<string>(),
                    Description = item["description"]?.Value<string>(),
                    TrackCount = item["tracks"]?["total"]?.Value<int>() ?? 0,
                    ThumbnailUrl = item["images"]?[0]?["url"]?.Value<string>()
                });
            }

            return result;
        }

        public async Task<List<StreamingTrack>> GetPlaylistTracksAsync(string playlistId, CancellationToken ct = default)
        {
            string token = await _tokens.GetValidAccessTokenAsync(ct);
            var result = new List<StreamingTrack>();
            string url = $"https://api.spotify.com/v1/playlists/{playlistId}/tracks?limit=100";

            while (url != null)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var resp = await _http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode) break;

                var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
                foreach (var item in json["items"] ?? new JArray())
                {
                    var track = item["track"];
                    if (track == null || track.Type == Newtonsoft.Json.Linq.JTokenType.Null) continue;
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

                url = json["next"]?.Value<string>();
            }

            return result;
        }

        public void Dispose() => _http.Dispose();
    }
}
