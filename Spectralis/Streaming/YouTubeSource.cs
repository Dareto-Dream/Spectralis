using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Spectralis.Streaming
{
    public class YouTubeSource : IStreamingSource
    {
        public string Name => "YouTube";
        public bool IsAuthenticated => File.Exists(_ytDlpPath);

        private readonly string _ytDlpPath;
        private bool _disposed;

        public YouTubeSource(string ytDlpPath = "yt-dlp")
        {
            _ytDlpPath = ytDlpPath;
        }

        public Task AuthenticateAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<StreamingTrack> SearchAsync(string query, CancellationToken ct = default)
        {
            var url = $"ytsearch1:{query}";
            var info = await RunYtDlpAsync($"--dump-json --no-playlist \"{url}\"", ct);
            if (string.IsNullOrWhiteSpace(info)) return null;

            var json = Newtonsoft.Json.Linq.JObject.Parse(info);
            return new StreamingTrack
            {
                Id = json["id"]?.Value<string>(),
                Title = json["title"]?.Value<string>(),
                Artist = json["uploader"]?.Value<string>(),
                Duration = TimeSpan.FromSeconds(json["duration"]?.Value<double>() ?? 0),
                ThumbnailUrl = json["thumbnail"]?.Value<string>(),
                StreamUrl = $"https://www.youtube.com/watch?v={json["id"]?.Value<string>()}",
                Source = "YouTube"
            };
        }

        public async Task<WaveStream> OpenStreamAsync(StreamingTrack track, CancellationToken ct = default)
        {
            var streamUrl = await RunYtDlpAsync($"-x --audio-format mp3 -g \"{track.StreamUrl}\"", ct);
            if (string.IsNullOrWhiteSpace(streamUrl))
                return null;

            return new MediaFoundationReader(streamUrl.Trim());
        }

        private async Task<string> RunYtDlpAsync(string args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(_ytDlpPath, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync(ct);
            return output;
        }

        public void Dispose() { _disposed = true; }
    }
}
