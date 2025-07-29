using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Spectralis.Core.Streaming
{
    public class SoundCloudSource : IStreamingSource
    {
        private readonly string _clientId;
        private readonly HttpClient _http;
        private bool _authenticated;

        public string Name => "SoundCloud";
        public bool IsAuthenticated => _authenticated;

        public SoundCloudSource(string clientId)
        {
            _clientId = clientId;
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", "Spectralis/5.0");
        }

        public async Task<IReadOnlyList<StreamingTrack>> SearchAsync(string query, int limit = 20, CancellationToken ct = default)
        {
            string encoded = Uri.EscapeDataString(query);
            string url = $"https://api-v2.soundcloud.com/search/tracks?q={encoded}&limit={limit}&client_id={_clientId}";

            var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();

            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var result = new List<StreamingTrack>();

            if (!doc.RootElement.TryGetProperty("collection", out var collection)) return result;

            foreach (var item in collection.EnumerateArray())
            {
                string? thumb = item.TryGetProperty("artwork_url", out var art) ? art.GetString() : null;
                if (thumb != null) thumb = thumb.Replace("-large.", "-t300x300.");

                string? username = null;
                if (item.TryGetProperty("user", out var user) && user.TryGetProperty("username", out var un))
                    username = un.GetString();

                long durMs = item.TryGetProperty("duration", out var d) ? d.GetInt64() : 0;

                result.Add(new StreamingTrack(
                    Id: item.TryGetProperty("id", out var id) ? id.GetRawText() : "",
                    Title: item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                    Artist: username ?? "",
                    Album: "",
                    Duration: TimeSpan.FromMilliseconds(durMs),
                    ThumbnailUrl: thumb,
                    Source: Name,
                    StreamUrl: item.TryGetProperty("permalink_url", out var pl) ? pl.GetString() : null
                ));
            }

            return result;
        }

        public Task<Stream> OpenStreamAsync(StreamingTrack track, CancellationToken ct = default)
        {
            throw new NotSupportedException("SoundCloud stream resolve requires a valid OAuth token or yt-dlp");
        }

        public Task<bool> AuthenticateAsync(CancellationToken ct = default)
        {
            _authenticated = !string.IsNullOrEmpty(_clientId);
            return Task.FromResult(_authenticated);
        }

        public void Dispose() => _http.Dispose();
    }
}
