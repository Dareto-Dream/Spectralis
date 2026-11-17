using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Spectralis.Core.Integrations.Spotify;

public sealed record SpotifyPlaylistContents(string Id, string Name, string? ImageUrl, string? SnapshotId,
    IReadOnlyList<SpotifyTrackResult> Tracks, bool HasFullAccess, int TotalItems);

public sealed partial class SpotifyService
{
    public async Task<SpotifyPlaylistContents?> GetAlbumContentsAsync(string clientId, string albumId, CancellationToken ct = default)
    {
        if (!SpotifyMatchup.IsTrackId(albumId)) throw new ArgumentException("Invalid Spotify album ID.", nameof(albumId));
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null) return null;
        using var metadata = await ReadPlaylistApiAsync($"{ApiBase}/albums/{albumId}", token, ct);
        if (metadata is null) return null;
        var root = metadata.RootElement;
        var name = root.GetProperty("name").GetString() ?? "Spotify album";
        var image = root.TryGetProperty("images", out var images) && images.GetArrayLength() > 0 ? images[0].GetProperty("url").GetString() : null;
        var tracks = new List<SpotifyTrackResult>();
        var visited = new HashSet<string>();
        string? next = $"{ApiBase}/albums/{albumId}/tracks?limit=50";
        var total = 0;
        while (next is not null)
        {
            if (!visited.Add(next) || visited.Count > 250) throw new InvalidOperationException("Spotify repeated an album page.");
            using var page = await ReadPlaylistApiAsync(next, token, ct);
            if (page is null) return new(albumId, name, image, null, tracks, false, total);
            total = page.RootElement.GetProperty("total").GetInt32();
            foreach (var item in page.RootElement.GetProperty("items").EnumerateArray())
                if (ReadSearchTrack(item) is { } track) tracks.Add(track with { Album = name, AlbumArtUrl = image });
            next = page.RootElement.TryGetProperty("next", out var url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null;
        }
        return new(albumId, name, image, null, tracks, true, total);
    }

    /// <summary>API metadata and ordered items. Public playlists may expose metadata without granting item access.</summary>
    public async Task<SpotifyPlaylistContents?> GetPlaylistContentsAsync(string clientId, string playlistId, CancellationToken ct = default)
    {
        if (!SpotifyMatchup.IsTrackId(playlistId)) throw new ArgumentException("Invalid Spotify playlist ID.", nameof(playlistId));
        var token = await GetFreshAccessTokenAsync(clientId);
        if (token is null) return null;
        using var metadata = await ReadPlaylistApiAsync($"{ApiBase}/playlists/{playlistId}", token, ct);
        if (metadata is null) return null;
        var summary = ReadPlaylistSummary(metadata.RootElement) ?? throw new InvalidOperationException("Spotify returned an invalid playlist.");
        var tracks = new List<SpotifyTrackResult>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? next = $"{ApiBase}/playlists/{playlistId}/items?limit=50";
        var total = summary.TrackCount;
        while (next is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (!visited.Add(next)) throw new InvalidOperationException("Spotify repeated a playlist page. Try refreshing the playlist.");
            using var page = await ReadPlaylistApiAsync(next, token, ct);
            if (page is null) return new(playlistId, summary.Name, summary.ImageUrl, summary.SnapshotId, tracks, false, total);
            if (page.RootElement.TryGetProperty("total", out var count) && count.TryGetInt32(out var value)) total = value;
            if (!page.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Spotify did not return the playlist items.");
            foreach (var item in items.EnumerateArray())
                if (ReadPlaylistTrack(item) is { } track) tracks.Add(track);
            next = page.RootElement.TryGetProperty("next", out var url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null;
            if (visited.Count > 250) throw new InvalidOperationException("The playlist exceeded Spotify's supported page limit.");
        }
        return new(playlistId, summary.Name, summary.ImageUrl, summary.SnapshotId, tracks, true, total);
    }

    private async Task<JsonDocument?> ReadPlaylistApiAsync(string url, string token, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "api.spotify.com" || !uri.IsDefaultPort
            || !(uri.AbsolutePath.StartsWith("/v1/playlists/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/v1/albums/", StringComparison.Ordinal)) || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("Spotify returned an unexpected playlist page URL.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _playbackHttp.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) return null;
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
    }
}
