using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Core.Models;

namespace Spectralis.Core.Scrobbling
{
    public class LastFmScrobbler : IScrobbler
    {
        private readonly string _apiKey;
        private readonly string _apiSecret;
        private readonly HttpClient _http;
        private string? _sessionKey;

        public string ServiceName => "Last.fm";
        public bool IsAuthenticated => !string.IsNullOrEmpty(_sessionKey);

        public LastFmScrobbler(string apiKey, string apiSecret)
        {
            _apiKey = apiKey;
            _apiSecret = apiSecret;
            _http = new HttpClient();
        }

        public void SetSession(string sessionKey) => _sessionKey = sessionKey;

        public Task<bool> AuthenticateAsync(CancellationToken ct = default)
            => Task.FromResult(IsAuthenticated);

        public async Task<bool> NowPlayingAsync(TrackInfo track, CancellationToken ct = default)
        {
            if (!IsAuthenticated) return false;
            var ps = BaseParams("track.updateNowPlaying");
            ps["track"] = track.Title;
            ps["artist"] = track.Artist;
            ps["album"] = track.Album;
            ps["duration"] = ((int)track.Duration.TotalSeconds).ToString();
            ps["api_sig"] = Sign(ps);
            return await PostAsync(ps, ct);
        }

        public async Task<bool> ScrobbleAsync(ScrobbleEntry entry, CancellationToken ct = default)
        {
            if (!IsAuthenticated) return false;
            var ps = BaseParams("track.scrobble");
            ps["track"] = entry.Track.Title;
            ps["artist"] = entry.Track.Artist;
            ps["album"] = entry.Track.Album;
            ps["timestamp"] = entry.Timestamp.ToUnixTimeSeconds().ToString();
            ps["duration"] = entry.DurationSeconds.ToString();
            ps["api_sig"] = Sign(ps);
            return await PostAsync(ps, ct);
        }

        private Dictionary<string, string> BaseParams(string method) => new()
        {
            ["method"] = method,
            ["api_key"] = _apiKey,
            ["sk"] = _sessionKey!,
            ["format"] = "json"
        };

        private string Sign(Dictionary<string, string> ps)
        {
            var sorted = new SortedDictionary<string, string>(ps);
            sorted.Remove("format");
            var sb = new StringBuilder();
            foreach (var kv in sorted) sb.Append(kv.Key).Append(kv.Value);
            sb.Append(_apiSecret);
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private async Task<bool> PostAsync(Dictionary<string, string> ps, CancellationToken ct)
        {
            try
            {
                var content = new FormUrlEncodedContent(ps);
                var resp = await _http.PostAsync("https://ws.audioscrobbler.com/2.0/", content, ct);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public void Dispose() => _http.Dispose();
    }
}
