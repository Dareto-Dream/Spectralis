using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Spectralis.Core.Audio;

namespace Spectralis.Core.Scrobbling
{
    public class ListenBrainzScrobbler : IScrobbler
    {
        private static readonly HttpClient _http = new();
        private readonly string _userToken;
        private const string BaseUrl = "https://api.listenbrainz.org/1/";

        public string Name => "ListenBrainz";
        public bool IsConfigured => !string.IsNullOrWhiteSpace(_userToken);

        public ListenBrainzScrobbler(string userToken) => _userToken = userToken;

        public async Task NowPlayingAsync(TrackInfo track)
        {
            if (!IsConfigured) return;
            var payload = BuildPayload("playing_now", track);
            await PostAsync(payload);
        }

        public async Task ScrobbleAsync(TrackInfo track, DateTime playedAt)
        {
            if (!IsConfigured) return;
            var payload = BuildPayload("single", track, ((DateTimeOffset)playedAt).ToUnixTimeSeconds());
            await PostAsync(payload);
        }

        private object BuildPayload(string listenType, TrackInfo track, long? timestamp = null)
        {
            var trackMetadata = new
            {
                artist_name = track.Artist ?? string.Empty,
                track_name = track.Title ?? string.Empty,
                release_name = track.Album ?? string.Empty
            };

            var listen = timestamp.HasValue
                ? (object)new { listened_at = timestamp.Value, track_metadata = trackMetadata }
                : new { track_metadata = trackMetadata };

            return new { listen_type = listenType, payload = new[] { listen } };
        }

        private async Task PostAsync(object payload)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "submit-listens")
                {
                    Headers = { { "Authorization", $"Token {_userToken}" } },
                    Content = JsonContent.Create(payload)
                };
                using var resp = await _http.SendAsync(req);
                resp.EnsureSuccessStatusCode();
            }
            catch { }
        }
    }
}
