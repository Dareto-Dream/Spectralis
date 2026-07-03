using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Spectralis.Core.Integrations.Spotify;

public sealed class SpotifyService : IDisposable
{
    private const string AuthBase = "https://accounts.spotify.com";
    private const string ApiBase = "https://api.spotify.com/v1";
    private const string Scopes = "streaming user-read-email user-read-private user-read-playback-state user-modify-playback-state " +
        "playlist-read-private playlist-read-collaborative playlist-modify-public playlist-modify-private";

    private static readonly HttpClient Http = new();

    private SpotifyTokenStore tokens;

    public SpotifyService()
    {
        tokens = SpotifyTokenStore.Load();
    }

    public bool IsLinked => !string.IsNullOrEmpty(tokens.RefreshToken);
    public string? AccountDisplayName => tokens.AccountDisplayName;
    public string? AccountEmail => tokens.AccountEmail;

    public async Task<bool> LinkAccountAsync(string clientId)
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
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            // Forces the consent screen even for an already-authorized app, so a user who
            // linked before the playlist scopes existed actually gets asked to grant them
            // instead of silently getting back a token that still lacks them.
            $"&show_dialog=true";

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
        tokens.Scope = tokenData.Value.Scope;
        tokens.Save();
        return true;
    }

    /// <summary>True once the linked token's granted scopes cover playlist read/write. A token
    /// saved before these scopes existed has a null/empty <see cref="SpotifyTokenStore.Scope"/>,
    /// so this reads false and callers should prompt the user to relink via <see cref="LinkAccountAsync"/>.</summary>
    public bool HasPlaylistScopes =>
        !string.IsNullOrEmpty(tokens.Scope) &&
        tokens.Scope.Contains("playlist-read-private", StringComparison.Ordinal) &&
        tokens.Scope.Contains("playlist-modify-private", StringComparison.Ordinal);

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

    public async Task<IReadOnlyList<SpotifyPlaylistSummary>> GetPlaylistsAsync(string clientId)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null) return [];

        return await FetchPaginatedAsync($"{ApiBase}/me/playlists?limit=50", token, "items", ReadPlaylistSummary);
    }

    public async Task<IReadOnlyList<SpotifyTrackResult>> GetPlaylistTracksAsync(string clientId, string playlistId)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null || string.IsNullOrWhiteSpace(playlistId)) return [];

        var url = $"{ApiBase}/playlists/{Uri.EscapeDataString(playlistId)}/tracks?limit=100";
        return await FetchPaginatedAsync(url, token, "items", ReadPlaylistTrack);
    }

    public async Task<IReadOnlyList<SpotifyTrackResult>> SearchTracksAsync(string clientId, string query, int limit = 20)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null || string.IsNullOrWhiteSpace(query)) return [];

        var url = $"{ApiBase}/search?type=track&limit={Math.Clamp(limit, 1, 50)}&q={Uri.EscapeDataString(query.Trim())}";
        return await FetchPaginatedAsync(url, token, "items", ReadSearchTrack, containerProperty: "tracks", maxItems: limit);
    }

    /// <summary>Appends tracks to the end of a Spotify playlist. Returns the new snapshot id on
    /// success, or null on failure — callers must refresh their cached snapshot id either way
    /// before the next write, since a stale one is rejected by Spotify.</summary>
    public async Task<string?> AddPlaylistItemsAsync(string clientId, string playlistId, IReadOnlyList<string> trackUris)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null || string.IsNullOrWhiteSpace(playlistId) || trackUris.Count == 0) return null;

        var body = JsonSerializer.Serialize(new { uris = trackUris });
        return await SendPlaylistWriteAsync(HttpMethod.Post, $"/playlists/{Uri.EscapeDataString(playlistId)}/tracks", token, body);
    }

    /// <summary>Replaces a Spotify playlist's entire track list in one call — the simplest way to
    /// push an arbitrary set of local add/remove/reorder edits back at once, rather than diffing
    /// them into separate add/remove/reorder requests. Spotify caps this endpoint at 100 uris per
    /// call; callers with larger playlists would need to chain add calls for the remainder, which
    /// this doesn't attempt since Spectralis-edited playlists aren't expected to hit that size.</summary>
    public async Task<string?> ReplacePlaylistItemsAsync(string clientId, string playlistId, IReadOnlyList<string> trackUris)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null || string.IsNullOrWhiteSpace(playlistId)) return null;

        var body = JsonSerializer.Serialize(new { uris = trackUris.Take(100) });
        return await SendPlaylistWriteAsync(HttpMethod.Put, $"/playlists/{Uri.EscapeDataString(playlistId)}/tracks", token, body);
    }

    /// <summary>Removes specific tracks from a Spotify playlist, guarded by <paramref name="snapshotId"/>
    /// so a stale local copy can't silently clobber a concurrent edit made elsewhere.</summary>
    public async Task<string?> RemovePlaylistItemsAsync(string clientId, string playlistId, IReadOnlyList<string> trackUris, string snapshotId)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null || string.IsNullOrWhiteSpace(playlistId) || trackUris.Count == 0) return null;

        var body = JsonSerializer.Serialize(new
        {
            tracks = trackUris.Select(uri => new { uri }).ToArray(),
            snapshot_id = snapshotId,
        });
        return await SendPlaylistWriteAsync(HttpMethod.Delete, $"/playlists/{Uri.EscapeDataString(playlistId)}/tracks", token, body);
    }

    /// <summary>Moves a contiguous run of tracks within a Spotify playlist, guarded by <paramref name="snapshotId"/>.</summary>
    public async Task<string?> ReorderPlaylistItemsAsync(string clientId, string playlistId, int rangeStart, int insertBefore, string snapshotId, int rangeLength = 1)
    {
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null || string.IsNullOrWhiteSpace(playlistId)) return null;

        var body = JsonSerializer.Serialize(new
        {
            range_start = rangeStart,
            range_length = rangeLength,
            insert_before = insertBefore,
            snapshot_id = snapshotId,
        });
        return await SendPlaylistWriteAsync(HttpMethod.Put, $"/playlists/{Uri.EscapeDataString(playlistId)}/tracks", token, body);
    }

    private static async Task<string?> SendPlaylistWriteAsync(HttpMethod method, string path, string token, string json)
    {
        try
        {
            using var req = new HttpRequestMessage(method, ApiBase + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("snapshot_id", out var snapEl) ? snapEl.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>Follows Spotify's cursor-paginated "next" URL until exhausted or <paramref name="maxItems"/>
    /// is hit. <paramref name="containerProperty"/> unwraps one nesting level first (search's
    /// paging object lives under "tracks", unlike playlist endpoints which page at the root).</summary>
    private static async Task<List<T>> FetchPaginatedAsync<T>(
        string initialUrl, string token, string itemsProperty, Func<JsonElement, T?> parseItem,
        string? containerProperty = null, int maxItems = 500) where T : class
    {
        var results = new List<T>();
        var url = initialUrl;
        try
        {
            while (url is not null && results.Count < maxItems)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var resp = await Http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) break;

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                if (containerProperty is not null && !root.TryGetProperty(containerProperty, out root))
                {
                    break;
                }

                if (root.TryGetProperty(itemsProperty, out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var itemEl in itemsEl.EnumerateArray())
                    {
                        var parsed = parseItem(itemEl);
                        if (parsed is not null)
                            results.Add(parsed);
                    }
                }

                url = root.TryGetProperty("next", out var nextEl) && nextEl.ValueKind == JsonValueKind.String
                    ? nextEl.GetString()
                    : null;
            }
        }
        catch { }
        return results;
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

        var (artists, album, artUrl) = ReadTrackDetails(element);
        return new SpotifyPlaybackTrack(
            element.TryGetProperty("id", out var idEl) ? idEl.GetString() : null,
            element.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "",
            artists,
            album,
            artUrl,
            element.TryGetProperty("duration_ms", out var durationEl) && durationEl.ValueKind == JsonValueKind.Number
                ? durationEl.GetInt32()
                : 0);
    }

    private static SpotifyPlaylistSummary? ReadPlaylistSummary(JsonElement element)
    {
        var id = element.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) return null;

        string? imageUrl = null;
        if (element.TryGetProperty("images", out var imagesEl) && imagesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var imageEl in imagesEl.EnumerateArray())
            {
                imageUrl = imageEl.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
                if (!string.IsNullOrWhiteSpace(imageUrl))
                    break;
            }
        }

        var ownerId = element.TryGetProperty("owner", out var ownerEl) && ownerEl.ValueKind == JsonValueKind.Object &&
            ownerEl.TryGetProperty("id", out var ownerIdEl)
            ? ownerIdEl.GetString()
            : null;

        var trackCount = element.TryGetProperty("tracks", out var tracksEl) && tracksEl.ValueKind == JsonValueKind.Object &&
            tracksEl.TryGetProperty("total", out var totalEl) && totalEl.ValueKind == JsonValueKind.Number
            ? totalEl.GetInt32()
            : 0;

        return new SpotifyPlaylistSummary(
            id,
            element.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "",
            imageUrl,
            element.TryGetProperty("snapshot_id", out var snapEl) ? snapEl.GetString() : null,
            trackCount,
            element.TryGetProperty("collaborative", out var collabEl) && collabEl.GetBoolean(),
            ownerId);
    }

    /// <summary>Unwraps a playlist-tracks page item (each one wraps the actual track under "track")
    /// before delegating to the same parsing <see cref="ReadSearchTrack"/> uses.</summary>
    private static SpotifyTrackResult? ReadPlaylistTrack(JsonElement itemEl) =>
        itemEl.TryGetProperty("track", out var trackEl) && trackEl.ValueKind == JsonValueKind.Object
            ? ReadSearchTrack(trackEl)
            : null;

    private static SpotifyTrackResult? ReadSearchTrack(JsonElement trackEl)
    {
        if (trackEl.TryGetProperty("type", out var typeEl) &&
            !string.Equals(typeEl.GetString(), "track", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var uri = trackEl.TryGetProperty("uri", out var uriEl) ? uriEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(uri)) return null;

        var (artists, album, artUrl) = ReadTrackDetails(trackEl);
        return new SpotifyTrackResult(
            uri,
            trackEl.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "",
            artists,
            album,
            artUrl,
            trackEl.TryGetProperty("duration_ms", out var durationEl) && durationEl.ValueKind == JsonValueKind.Number
                ? durationEl.GetInt32()
                : 0);
    }

    private static (string Artists, string? Album, string? ArtUrl) ReadTrackDetails(JsonElement trackEl)
    {
        var artistNames = new List<string>();
        if (trackEl.TryGetProperty("artists", out var artistsEl) && artistsEl.ValueKind == JsonValueKind.Array)
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
        if (trackEl.TryGetProperty("album", out var albumEl) && albumEl.ValueKind == JsonValueKind.Object)
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

        return (string.Join(", ", artistNames), albumName, albumArtUrl);
    }

    private static async Task<(string AccessToken, string RefreshToken, int ExpiresIn, string? Scope)?> ExchangeCodeAsync(
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
                root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600,
                root.TryGetProperty("scope", out var sc) ? sc.GetString() : null
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

    public void Dispose() { }
}

public sealed record SpotifyDevice(
    string? Id,
    string? Name,
    string? Type,
    bool IsActive,
    bool IsRestricted);

public sealed record SpotifyPlaybackTrack(
    string? Id,
    string Name,
    string? Artist,
    string? Album,
    string? AlbumArtUrl,
    int DurationMs);

public sealed record SpotifyPlaybackSnapshot(
    bool IsPlaying,
    int ProgressMs,
    SpotifyDevice? Device,
    SpotifyPlaybackTrack? Track);

public sealed record SpotifyQueueSnapshot(
    SpotifyPlaybackTrack? Current,
    IReadOnlyList<SpotifyPlaybackTrack> Queue);

public sealed record SpotifyPlaylistSummary(
    string Id,
    string Name,
    string? ImageUrl,
    string? SnapshotId,
    int TrackCount,
    bool Collaborative,
    string? OwnerId);

public sealed record SpotifyTrackResult(
    string Uri,
    string Name,
    string? Artist,
    string? Album,
    string? AlbumArtUrl,
    int DurationMs);
