using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Newtonsoft.Json.Linq;

namespace Spectralis.Streaming
{
    public class SoundCloudSource : IStreamingSource
    {
        public string Name => "SoundCloud";
        public bool IsAuthenticated => !string.IsNullOrEmpty(_clientId);

        private readonly string _clientId;
        private readonly HttpClient _http;
        private bool _disposed;

        public SoundCloudSource(string clientId)
        {
            _clientId = clientId;
            _http = new HttpClient();
        }

        public Task AuthenticateAsync(CancellationToken ct = default) =>
            Task.CompletedTask;

        public async Task<StreamingTrack> SearchAsync(string query, CancellationToken ct = default)
        {
            var url = $"https://api.soundcloud.com/tracks?q={Uri.EscapeDataString(query)}&limit=1&client_id={_clientId}";
            var json = await _http.GetStringAsync(url);
            var arr = JArray.Parse(json);

            if (arr.Count == 0) return null;

            var item = arr[0];
            var user = item["user"];
            string title = item["title"]?.Value<string>();
            string artist = user?["username"]?.Value<string>();

            return new StreamingTrack
            {
                Id = item["id"]?.Value<string>(),
                Title = title,
                Artist = artist,
                Duration = TimeSpan.FromMilliseconds(item["duration"]?.Value<long>() ?? 0),
                ThumbnailUrl = item["artwork_url"]?.Value<string>(),
                StreamUrl = item["stream_url"]?.Value<string>() + $"?client_id={_clientId}",
                Source = "SoundCloud"
            };
        }

        public async Task<WaveStream> OpenStreamAsync(StreamingTrack track, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(track.StreamUrl))
                throw new InvalidOperationException("No stream URL for this SoundCloud track.");

            var stream = await _http.GetStreamAsync(track.StreamUrl);
            return new RawSourceWaveStream(stream, new WaveFormat(44100, 16, 2));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http.Dispose();
        }
    }
}
