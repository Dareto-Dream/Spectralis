using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Spectralis.Core.Scrobbling;

/// <summary>MD5-signed Last.fm API client: token/session auth, now-playing, scrobble batches.</summary>
public sealed class LastFmClient
{
    private static readonly HttpClient Http = new();
    private const string ApiBase = "https://ws.audioscrobbler.com/2.0/";

    static LastFmClient()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Spectralis/1.0 (steak.nuggetincorperated@gmail.com)");
    }

    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _sessionKey;

    public LastFmClient(string apiKey, string apiSecret, string sessionKey)
    {
        _apiKey = apiKey;
        _apiSecret = apiSecret;
        _sessionKey = sessionKey;
    }

    public static string BuildAuthorizeUrl(string apiKey, string token) =>
        $"https://www.last.fm/api/auth/?api_key={Uri.EscapeDataString(apiKey)}&token={Uri.EscapeDataString(token)}";

    public static async Task<string?> GetTokenAsync(string apiKey, string apiSecret, CancellationToken ct = default)
    {
        var signed = Sign(new Dictionary<string, string>
        {
            ["method"] = "auth.getToken",
            ["api_key"] = apiKey,
        }, apiSecret);
        signed["format"] = "json";

        var resp = await Http.PostAsync(ApiBase, new FormUrlEncodedContent(signed), ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("token", out var tok) ? tok.GetString() : null;
    }

    public static async Task<(string? SessionKey, string? Username)> GetSessionAsync(
        string apiKey, string apiSecret, string token, CancellationToken ct = default)
    {
        var signed = Sign(new Dictionary<string, string>
        {
            ["method"] = "auth.getSession",
            ["api_key"] = apiKey,
            ["token"] = token,
        }, apiSecret);
        signed["format"] = "json";

        var resp = await Http.PostAsync(ApiBase, new FormUrlEncodedContent(signed), ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("session", out var session))
        {
            return (null, null);
        }

        var key = session.TryGetProperty("key", out var k) ? k.GetString() : null;
        var name = session.TryGetProperty("name", out var n) ? n.GetString() : null;
        return (key, name);
    }

    public async Task<bool> UpdateNowPlayingAsync(
        string title, string artist, string album, double durationSeconds,
        CancellationToken ct = default)
    {
        var p = new Dictionary<string, string>
        {
            ["method"] = "track.updateNowPlaying",
            ["api_key"] = _apiKey,
            ["sk"] = _sessionKey,
            ["track"] = title,
            ["artist"] = artist,
            ["album"] = album,
            ["duration"] = ((int)durationSeconds).ToString(),
        };
        var signed = Sign(p, _apiSecret);
        signed["format"] = "json";

        try
        {
            var resp = await Http.PostAsync(ApiBase, new FormUrlEncodedContent(signed), ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ScrobbleAsync(IList<ScrobbleRecord> records, CancellationToken ct = default)
    {
        if (records.Count == 0)
        {
            return true;
        }

        // Last.fm accepts up to 50 scrobbles per batch.
        var batch = records.Take(50).ToList();
        var p = new Dictionary<string, string>
        {
            ["method"] = "track.scrobble",
            ["api_key"] = _apiKey,
            ["sk"] = _sessionKey,
        };
        for (var i = 0; i < batch.Count; i++)
        {
            p[$"artist[{i}]"] = batch[i].Artist;
            p[$"track[{i}]"] = batch[i].Title;
            p[$"timestamp[{i}]"] = batch[i].Timestamp.ToString();
            if (!string.IsNullOrWhiteSpace(batch[i].Album))
            {
                p[$"album[{i}]"] = batch[i].Album;
            }
        }

        var signed = Sign(p, _apiSecret);
        signed["format"] = "json";

        try
        {
            var resp = await Http.PostAsync(ApiBase, new FormUrlEncodedContent(signed), ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, string> Sign(Dictionary<string, string> p, string secret)
    {
        var sorted = p.OrderBy(kv => kv.Key, StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var kv in sorted)
        {
            sb.Append(kv.Key).Append(kv.Value);
        }

        sb.Append(secret);

        var hash = MD5.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        p["api_sig"] = Convert.ToHexString(hash).ToLowerInvariant();
        return p;
    }
}
