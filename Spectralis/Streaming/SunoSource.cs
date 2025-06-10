using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Newtonsoft.Json.Linq;

namespace Spectralis.Streaming
{
    public class SunoSource : IStreamingSource
    {
        public string Name => "Suno";
        public bool IsAuthenticated => !string.IsNullOrEmpty(_sessionToken);

        private readonly HttpClient _http;
        private string _sessionToken;
        private bool _disposed;

        public SunoSource()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", "Spectralis/1.0");
        }

        public Task AuthenticateAsync(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public void SetSessionToken(string token)
        {
            _sessionToken = token;
        }

        public async Task<StreamingTrack> SearchAsync(string query, CancellationToken ct = default)
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://studio-api.suno.ai/api/feed/?search={Uri.EscapeDataString(query)}");
            req.Headers.Add("Cookie", $"__session={_sessionToken}");

            var resp = await _http.SendAsync(req, ct);
            var json = JArray.Parse(await resp.Content.ReadAsStringAsync());
            if (json.Count == 0) return null;

            var item = json[0];
            return new StreamingTrack
            {
                Id = item["id"]?.Value<string>(),
                Title = item["title"]?.Value<string>(),
                Artist = item["display_name"]?.Value<string>() ?? "Suno AI",
                Duration = TimeSpan.FromSeconds(item["duration"]?.Value<double>() ?? 0),
                ThumbnailUrl = item["image_url"]?.Value<string>(),
                StreamUrl = item["audio_url"]?.Value<string>(),
                Source = "Suno"
            };
        }

        public async Task<WaveStream> OpenStreamAsync(StreamingTrack track, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(track.StreamUrl))
                throw new InvalidOperationException("No audio URL for this Suno track.");

            var stream = await _http.GetStreamAsync(track.StreamUrl);
            return new Mp3FileReader(stream);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http.Dispose();
        }
    }
}
