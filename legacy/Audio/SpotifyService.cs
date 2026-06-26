using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Spectralis;

internal sealed class SpotifyService : IDisposable
{
    private const string AuthBase = "https://accounts.spotify.com";
    private const string ApiBase = "https://api.spotify.com/v1";
    private const string Scopes = "streaming user-read-email user-read-private user-read-playback-state user-modify-playback-state";

    private static readonly HttpClient Http = new();

    private SpotifyTokenStore tokens;

    public SpotifyService()
    {
        tokens = SpotifyTokenStore.Load();
    }

    public bool IsLinked => !string.IsNullOrEmpty(tokens.RefreshToken);
    public string? AccountDisplayName => tokens.AccountDisplayName;
    public string? AccountEmail => tokens.AccountEmail;

    public async Task<bool> LinkAccountAsync(string clientId, IWin32Window? owner = null)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return false;

        var verifier = GenerateCodeVerifier();
        var challenge = GenerateCodeChallenge(verifier);
        var state = GenerateRandom(16);

        var authUrl = $"{AuthBase}/authorize" +
            $"?response_type=code" +
            $"&client_id={Uri.EscapeDataString(clientId.Trim())}" +
            $"&scope={Uri.EscapeDataString(Scopes)}" +
            $"&redirect_uri={Uri.EscapeDataString(SpotifyAuthCallbackServer.RedirectUri)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge_method=S256" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}";

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var server = new SpotifyAuthCallbackServer();

        Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

        var result = await server.WaitForCallbackAsync(cts.Token);
        if (result is null || result.Value.State != state) return false;

        var tokenData = await ExchangeCodeAsync(clientId.Trim(), result.Value.Code, verifier);
        if (tokenData is null) return false;

        var profile = await GetProfileAsync(tokenData.Value.AccessToken);
        tokens.AccessToken = tokenData.Value.AccessToken;
        tokens.RefreshToken = tokenData.Value.RefreshToken;
        tokens.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenData.Value.ExpiresIn - 30);
        tokens.AccountDisplayName = profile?.DisplayName;
        tokens.AccountEmail = profile?.Email;
        tokens.Save();
        return true;
    }

    public void UnlinkAccount()
    {
        tokens.Clear();
        tokens = new SpotifyTokenStore();
    }

    public async Task<string?> GetFreshAccessTokenAsync(string clientId)
    {
        if (tokens.HasValidToken) return tokens.AccessToken;
        if (string.IsNullOrEmpty(tokens.RefreshToken) || string.IsNullOrEmpty(clientId)) return null;

        var refreshed = await RefreshAsync(clientId.Trim(), tokens.RefreshToken);
        if (refreshed is null) return null;

        tokens.AccessToken = refreshed.Value.AccessToken;
        tokens.ExpiresAt = DateTime.UtcNow.AddSeconds(refreshed.Value.ExpiresIn - 30);
        if (!string.IsNullOrEmpty(refreshed.Value.RefreshToken))
            tokens.RefreshToken = refreshed.Value.RefreshToken;
        tokens.Save();
        return tokens.AccessToken;
    }

    public async Task<bool> TransferPlaybackAsync(string deviceId, string clientId)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null) return false;
        var body = JsonSerializer.Serialize(new { device_ids = new[] { deviceId }, play = true });
        return await SendApiAsync(HttpMethod.Put, "/me/player", token, body);
    }

    public async Task<bool> ResumeAsync(string clientId, string? deviceId = null)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null) return false;
        var path = string.IsNullOrWhiteSpace(deviceId)
            ? "/me/player/play"
            : $"/me/player/play?device_id={Uri.EscapeDataString(deviceId)}";
        return await SendApiAsync(HttpMethod.Put, path, token, "{}");
    }

    public async Task<bool> PlayUriAsync(string playbackUri, string clientId, string? deviceId = null)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null) return false;

        var normalizedUri = playbackUri.Trim();
        if (!normalizedUri.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
            return false;

        var path = string.IsNullOrWhiteSpace(deviceId)
            ? "/me/player/play"
            : $"/me/player/play?device_id={Uri.EscapeDataString(deviceId)}";
        var body = IsSpotifyContextUri(normalizedUri)
            ? JsonSerializer.Serialize(new { context_uri = normalizedUri })
            : JsonSerializer.Serialize(new { uris = new[] { normalizedUri } });
        return await SendApiAsync(HttpMethod.Put, path, token, body);
    }

    private static bool IsSpotifyContextUri(string playbackUri)
    {
        var parts = playbackUri.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return false;

        return parts[1].Equals("album", StringComparison.OrdinalIgnoreCase) ||
            parts[1].Equals("artist", StringComparison.OrdinalIgnoreCase) ||
            parts[1].Equals("playlist", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> PauseAsync(string clientId, string? deviceId = null)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null) return false;
        var path = string.IsNullOrWhiteSpace(deviceId)
            ? "/me/player/pause"
            : $"/me/player/pause?device_id={Uri.EscapeDataString(deviceId)}";
        return await SendApiAsync(HttpMethod.Put, path, token, "{}");
    }

    public async Task<bool> NextTrackAsync(string clientId, string? deviceId = null)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null) return false;
        var path = string.IsNullOrWhiteSpace(deviceId)
            ? "/me/player/next"
            : $"/me/player/next?device_id={Uri.EscapeDataString(deviceId)}";
        return await SendApiAsync(HttpMethod.Post, path, token, "{}");
    }

    public async Task<bool> PreviousTrackAsync(string clientId, string? deviceId = null)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null) return false;
        var path = string.IsNullOrWhiteSpace(deviceId)
            ? "/me/player/previous"
            : $"/me/player/previous?device_id={Uri.EscapeDataString(deviceId)}";
        return await SendApiAsync(HttpMethod.Post, path, token, "{}");
    }

    public async Task<bool> SeekAsync(int positionMs, string clientId, string? deviceId = null)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null) return false;
        var path = $"/me/player/seek?position_ms={positionMs}";
        if (!string.IsNullOrWhiteSpace(deviceId))
            path += $"&device_id={Uri.EscapeDataString(deviceId)}";
        return await SendApiAsync(HttpMethod.Put, path, token, "{}");
    }

    public async Task<SpotifyPlaybackSnapshot?> GetPlaybackSnapshotAsync(string clientId)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null) return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/me/player");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await Http.SendAsync(req);
            if ((int)resp.StatusCode == 204 || !resp.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var device = root.TryGetProperty("device", out var deviceEl) && deviceEl.ValueKind == JsonValueKind.Object
                ? ReadDevice(deviceEl)
                : null;

            SpotifyPlaybackTrack? track = null;
            if (root.TryGetProperty("item", out var itemEl) && itemEl.ValueKind == JsonValueKind.Object)
                track = ReadTrack(itemEl);

            return new SpotifyPlaybackSnapshot(
                root.TryGetProperty("is_playing", out var isPlayingEl) && isPlayingEl.GetBoolean(),
                root.TryGetProperty("progress_ms", out var progressEl) && progressEl.ValueKind == JsonValueKind.Number
                    ? progressEl.GetInt32()
                    : 0,
                device,
                track);
        }
        catch { return null; }
    }

    public async Task<IReadOnlyList<SpotifyDevice>> GetAvailableDevicesAsync(string clientId)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null) return [];

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/me/player/devices");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return [];

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("devices", out var devicesEl) ||
                devicesEl.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var devices = new List<SpotifyDevice>();
            foreach (var deviceEl in devicesEl.EnumerateArray())
            {
                var device = ReadDevice(deviceEl);
                if (!string.IsNullOrWhiteSpace(device.Id))
                    devices.Add(device);
            }
            return devices;
        }
        catch { return []; }
    }

    public async Task<SpotifyQueueSnapshot?> GetQueueAsync(string clientId)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null) return null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/me/player/queue");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;

            SpotifyPlaybackTrack? current = null;
            if (root.TryGetProperty("currently_playing", out var currentEl) &&
                currentEl.ValueKind == JsonValueKind.Object)
            {
                current = ReadTrack(currentEl);
            }

            var queue = new List<SpotifyPlaybackTrack>();
            if (root.TryGetProperty("queue", out var queueEl) &&
                queueEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var itemEl in queueEl.EnumerateArray())
                {
                    if (itemEl.ValueKind != JsonValueKind.Object)
                        continue;

                    var track = ReadTrack(itemEl);
                    if (track is not null)
                        queue.Add(track);
                }
            }

            return new SpotifyQueueSnapshot(current, queue);
        }
        catch { return null; }
    }

    private static async Task<bool> SendApiAsync(HttpMethod method, string path, string token, string json)
    {
        try
        {
            using var req = new HttpRequestMessage(method, ApiBase + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(req);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static SpotifyDevice ReadDevice(JsonElement element) =>
        new(
            element.TryGetProperty("id", out var idEl) ? idEl.GetString() : null,
            element.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null,
            element.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null,
            element.TryGetProperty("is_active", out var activeEl) && activeEl.GetBoolean(),
            element.TryGetProperty("is_restricted", out var restrictedEl) && restrictedEl.GetBoolean());

    private static SpotifyPlaybackTrack? ReadTrack(JsonElement element)
    {
        if (element.TryGetProperty("type", out var typeEl) &&
            !string.Equals(typeEl.GetString(), "track", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var artistNames = new List<string>();
        if (element.TryGetProperty("artists", out var artistsEl) && artistsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var artistEl in artistsEl.EnumerateArray())
            {
                if (artistEl.TryGetProperty("name", out var artistNameEl))
                {
                    var artistName = artistNameEl.GetString();
                    if (!string.IsNullOrWhiteSpace(artistName))
                        artistNames.Add(artistName);
                }
            }
        }

        string? albumName = null;
        string? albumArtUrl = null;
        if (element.TryGetProperty("album", out var albumEl) && albumEl.ValueKind == JsonValueKind.Object)
        {
            albumName = albumEl.TryGetProperty("name", out var albumNameEl) ? albumNameEl.GetString() : null;
            if (albumEl.TryGetProperty("images", out var imagesEl) && imagesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var imageEl in imagesEl.EnumerateArray())
                {
                    albumArtUrl = imageEl.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(albumArtUrl))
                        break;
                }
            }
        }

        return new SpotifyPlaybackTrack(
            element.TryGetProperty("id", out var idEl) ? idEl.GetString() : null,
            element.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "",
            string.Join(", ", artistNames),
            albumName,
            albumArtUrl,
            element.TryGetProperty("duration_ms", out var durationEl) && durationEl.ValueKind == JsonValueKind.Number
                ? durationEl.GetInt32()
                : 0);
    }

    private static async Task<(string AccessToken, string RefreshToken, int ExpiresIn)?> ExchangeCodeAsync(
        string clientId, string code, string verifier)
    {
        try
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = SpotifyAuthCallbackServer.RedirectUri,
                ["client_id"] = clientId,
                ["code_verifier"] = verifier
            };
            var resp = await Http.PostAsync($"{AuthBase}/api/token", new FormUrlEncodedContent(form));
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (!root.TryGetProperty("access_token", out var at)) return null;
            return (
                at.GetString()!,
                root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "",
                root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600
            );
        }
        catch { return null; }
    }

    private static async Task<(string AccessToken, string? RefreshToken, int ExpiresIn)?> RefreshAsync(
        string clientId, string refreshToken)
    {
        try
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId
            };
            var resp = await Http.PostAsync($"{AuthBase}/api/token", new FormUrlEncodedContent(form));
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (!root.TryGetProperty("access_token", out var at)) return null;
            return (
                at.GetString()!,
                root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
                root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600
            );
        }
        catch { return null; }
    }

    private static async Task<(string? DisplayName, string? Email)?> GetProfileAsync(string token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/me");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await Http.SendAsync(req);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            return (
                root.TryGetProperty("display_name", out var dn) ? dn.GetString() : null,
                root.TryGetProperty("email", out var em) ? em.GetString() : null
            );
        }
        catch { return null; }
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Base64Url(bytes);
    }

    private static string GenerateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    private static string GenerateRandom(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        return Convert.ToHexString(bytes)[..length].ToLowerInvariant();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose()
    {
    }
}

internal sealed record SpotifyDevice(
    string? Id,
    string? Name,
    string? Type,
    bool IsActive,
    bool IsRestricted);

internal sealed record SpotifyPlaybackTrack(
    string? Id,
    string Name,
    string? Artist,
    string? Album,
    string? AlbumArtUrl,
    int DurationMs);

internal sealed record SpotifyPlaybackSnapshot(
    bool IsPlaying,
    int ProgressMs,
    SpotifyDevice? Device,
    SpotifyPlaybackTrack? Track);

internal sealed record SpotifyQueueSnapshot(
    SpotifyPlaybackTrack? Current,
    IReadOnlyList<SpotifyPlaybackTrack> Queue);
