using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Spectralis.Streaming
{
    public class SpotifyNowPlaying : IDisposable
    {
        private readonly SpotifyTokenManager _tokens;
        private readonly HttpClient _http;
        private bool _disposed;
        private Timer _pollTimer;

        public event EventHandler<StreamingTrack> TrackChanged;

        private string _lastTrackId;

        public SpotifyNowPlaying(SpotifyTokenManager tokens)
        {
            _tokens = tokens;
            _http = new HttpClient();
        }

        public void StartPolling(int intervalMs = 3000)
        {
            _pollTimer?.Dispose();
            _pollTimer = new Timer(_ => _ = PollAsync(), null, 0, intervalMs);
        }

        public void StopPolling()
        {
            _pollTimer?.Dispose();
            _pollTimer = null;
        }

        private async Task PollAsync()
        {
            try
            {
                var token = await _tokens.GetValidAccessTokenAsync();
                var req = new HttpRequestMessage(HttpMethod.Get,
                    "https://api.spotify.com/v1/me/player/currently-playing");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return;

                var content = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(content)) return;

                var json = JObject.Parse(content);
                var item = json["item"];
                if (item == null) return;

                string id = item["id"]?.Value<string>();
                if (id == _lastTrackId) return;
                _lastTrackId = id;

                TrackChanged?.Invoke(this, new StreamingTrack
                {
                    Id = id,
                    Title = item["name"]?.Value<string>(),
                    Artist = item["artists"]?[0]?["name"]?.Value<string>(),
                    Album = item["album"]?["name"]?.Value<string>(),
                    Duration = TimeSpan.FromMilliseconds(item["duration_ms"]?.Value<long>() ?? 0),
                    ThumbnailUrl = item["album"]?["images"]?[0]?["url"]?.Value<string>(),
                    Source = "Spotify"
                });
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pollTimer?.Dispose();
            _http.Dispose();
        }
    }
}
