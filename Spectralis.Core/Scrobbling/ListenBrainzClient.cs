using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Spectralis.Core.Scrobbling;

public sealed class ListenBrainzClient
{
    private static readonly HttpClient Http = new();
    private const string ApiBase = "https://api.listenbrainz.org";

    static ListenBrainzClient()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Spectralis/1.0 (steak.nuggetincorperated@gmail.com)");
    }

    private readonly string _token;

    public ListenBrainzClient(string token)
    {
        _token = token;
    }

    public async Task<bool> ValidateTokenAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/1/validate-token");
            req.Headers.Authorization = new AuthenticationHeaderValue("Token", _token);
            var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return false;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("valid", out var valid) && valid.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> GetUsernameAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/1/validate-token");
            req.Headers.Authorization = new AuthenticationHeaderValue("Token", _token);
            var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("user_name", out var name) ? name.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> SubmitNowPlayingAsync(
        string title, string artist, string album, CancellationToken ct = default)
    {
        var payload = new
        {
            listen_type = "playing_now",
            payload = new[]
            {
                new
                {
                    track_metadata = new
                    {
                        track_name = title,
                        artist_name = artist,
                        release_name = album,
                    },
                },
            },
        };
        return await PostAsync(payload, ct);
    }

    public async Task<bool> SubmitListenAsync(IList<ScrobbleRecord> records, CancellationToken ct = default)
    {
        if (records.Count == 0)
        {
            return true;
        }

        object payload;
        if (records.Count == 1)
        {
            payload = new
            {
                listen_type = "single",
                payload = new[]
                {
                    new
                    {
                        listened_at = records[0].Timestamp,
                        track_metadata = new
                        {
                            track_name = records[0].Title,
                            artist_name = records[0].Artist,
                            release_name = records[0].Album,
                        },
                    },
                },
            };
        }
        else
        {
            payload = new
            {
                listen_type = "import",
                payload = records.Select(r => new
                {
                    listened_at = r.Timestamp,
                    track_metadata = new
                    {
                        track_name = r.Title,
                        artist_name = r.Artist,
                        release_name = r.Album,
                    },
                }).ToArray(),
            };
        }

        return await PostAsync(payload, ct);
    }

    private async Task<bool> PostAsync(object payload, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/1/submit-listens");
            req.Headers.Authorization = new AuthenticationHeaderValue("Token", _token);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await Http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
