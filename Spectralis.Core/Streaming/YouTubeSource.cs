using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Spectralis.Core.Streaming
{
    public class YouTubeSource : IStreamingSource
    {
        private readonly string _apiKey;
        private readonly HttpClient _http;
        private bool _authenticated;

        public string Name => "YouTube";
        public bool IsAuthenticated => _authenticated;

        public YouTubeSource(string apiKey)
        {
            _apiKey = apiKey;
            _http = new HttpClient();
        }

        public async Task<IReadOnlyList<StreamingTrack>> SearchAsync(string query, int limit = 20, CancellationToken ct = default)
        {
            string encoded = Uri.EscapeDataString(query);
            string url = $"https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&videoCategoryId=10&maxResults={limit}&q={encoded}&key={_apiKey}";

            var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();

            var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var result = new List<StreamingTrack>();

            foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                var snippet = item.GetProperty("snippet");
                string videoId = item.GetProperty("id").GetProperty("videoId").GetString() ?? "";
                string title = snippet.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                string channel = snippet.TryGetProperty("channelTitle", out var ch) ? ch.GetString() ?? "" : "";
                string? thumb = null;
                if (snippet.TryGetProperty("thumbnails", out var thumbs) &&
                    thumbs.TryGetProperty("high", out var high) &&
                    high.TryGetProperty("url", out var u))
                    thumb = u.GetString();

                result.Add(new StreamingTrack(
                    Id: videoId,
                    Title: title,
                    Artist: channel,
                    Album: "",
                    Duration: TimeSpan.Zero,
                    ThumbnailUrl: thumb,
                    Source: Name,
                    StreamUrl: $"https://www.youtube.com/watch?v={videoId}"
                ));
            }

            return result;
        }

        public async Task<Stream> OpenStreamAsync(StreamingTrack track, CancellationToken ct = default)
        {
            string ytdlp = LocateYtDlp();
            if (ytdlp == null)
                throw new InvalidOperationException("yt-dlp not found. Install yt-dlp and ensure it is on PATH.");

            var psi = new System.Diagnostics.ProcessStartInfo(ytdlp,
                $"--get-url -f bestaudio/best -- {track.Id}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start yt-dlp process.");

            string url = (await proc.StandardOutput.ReadToEndAsync()).Trim();
            string err = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync(ct);

            if (string.IsNullOrEmpty(url))
                throw new InvalidOperationException(
                    $"yt-dlp returned no URL for {track.Id}." +
                    (string.IsNullOrEmpty(err) ? "" : $" stderr: {err}"));

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStreamAsync(ct);
        }

        private static string? LocateYtDlp()
        {
            foreach (var name in new[] { "yt-dlp", "yt-dlp.exe" })
            {
                string? found = System.Environment.GetEnvironmentVariable("PATH")?
                    .Split(System.IO.Path.PathSeparator)
                    .Select(dir => System.IO.Path.Combine(dir, name))
                    .FirstOrDefault(System.IO.File.Exists);
                if (found != null) return found;
            }
            return null;
        }

        public Task<bool> AuthenticateAsync(CancellationToken ct = default)
        {
            _authenticated = !string.IsNullOrEmpty(_apiKey);
            return Task.FromResult(_authenticated);
        }

        public void Dispose() => _http.Dispose();
    }
}
