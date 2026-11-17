using System.Text.Json;
using Spectralis.Core.Integrations;
using Spectralis.Core.Integrations.Spotify;
using Spectralis.Core.Playlists;

namespace Spectralis.App.Services;

public sealed class PlaylistImportService
{
    private static readonly HttpClient Http = Spectralis.Core.Diagnostics.NetworkClients.Create("playlist-import", TimeSpan.FromSeconds(30));
    private readonly Func<string, CancellationToken, Task<SpotifyPlaylistContents?>> spotifyPlaylist;
    private readonly Func<string, CancellationToken, Task<SpotifyPlaylistContents?>> spotifyAlbum;
    private readonly Func<string, CancellationToken, Task<string>> fetchPage;
    private readonly Func<string, CancellationToken, Task<string>> extractPlaylist;
    private readonly OpenUrlService urls = new();
    public PlaylistImportService(AppSettings settings,
        Func<string, CancellationToken, Task<SpotifyPlaylistContents?>>? spotifyPlaylist = null,
        Func<string, CancellationToken, Task<string>>? fetchPage = null,
        Func<string, CancellationToken, Task<string>>? extractPlaylist = null,
        Func<string, CancellationToken, Task<SpotifyPlaylistContents?>>? spotifyAlbum = null)
    {
        this.spotifyPlaylist = spotifyPlaylist ?? (async (id, ct) =>
        {
            using var spotify = new SpotifyService();
            return await spotify.GetPlaylistContentsAsync(SpotifyClientIdProvider.ResolveClientId(settings.SpotifyCustomClientId), id, ct);
        });
        this.spotifyAlbum = spotifyAlbum ?? (async (id, ct) =>
        {
            using var spotify = new SpotifyService();
            return await spotify.GetAlbumContentsAsync(SpotifyClientIdProvider.ResolveClientId(settings.SpotifyCustomClientId), id, ct);
        });
        this.fetchPage = fetchPage ?? ((url, ct) => Http.GetStringAsync(url, ct));
        this.extractPlaylist = extractPlaylist ?? ExtractAsync;
    }

    public async Task<Playlist> ImportAsync(string input, CancellationToken ct)
    {
        input = input.Trim();
        if (SpotifyPlaybackLink.TryParse(input, out var spotify)) return await ImportSpotifyAsync(spotify, ct);
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Paste a supported playlist or track link.");
        if (LooksLikeCollection(uri)) return ParseExtractorPlaylist(await extractPlaylist(input, ct), input);
        var resolved = await urls.ResolveAsync(input, ct, quickOnly: true);
        return new Playlist { Name = resolved.Title ?? "Imported tracks", SourceUrl = input, SourceName = resolved.ServiceLabel,
            IsReadOnlySource = true, Items = [FromResolved(resolved)] };
    }

    public static bool LooksLikeCollection(Uri uri) => uri.AbsolutePath.Contains("/playlist", StringComparison.OrdinalIgnoreCase)
        || uri.AbsolutePath.Contains("/sets/", StringComparison.OrdinalIgnoreCase)
        || uri.AbsolutePath.Contains("/album/", StringComparison.OrdinalIgnoreCase)
        || uri.AbsolutePath.Contains("/albums/", StringComparison.OrdinalIgnoreCase)
        || uri.Query.TrimStart('?').Split('&').Any(p => p.StartsWith("list=", StringComparison.OrdinalIgnoreCase))
        || uri.Host.EndsWith("untitled.stream", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.StartsWith("/s/", StringComparison.Ordinal);

    private async Task<Playlist> ImportSpotifyAsync(SpotifyPlaybackLink link, CancellationToken ct)
    {
        var parts = link.PlaybackUri.Split(':');
        if (parts[1] == "track")
        {
            var facts = SpotifyMatchup.ParseTrackEmbed(await fetchPage(SpotifyMatchup.EmbedUrl("track", parts[2]), ct));
            return new Playlist { Name = facts?.Title ?? "Spotify track", SourceName = "Spotify", Items = [new PlaylistItem
            { SpotifyTrackUri = link.PlaybackUri, Title = facts?.Title, Artist = facts?.Artist, DurationSeconds = (facts?.DurationMs ?? 0) / 1000d, AlbumArtUrl = facts?.ArtworkUrl }] };
        }
        if (parts[1] is not ("playlist" or "album")) throw new InvalidOperationException("Use a Spotify playlist, album, or track link.");
        SpotifyPlaylistContents? api = null;
        string? apiNotice = null;
        
        {
            try { api = await (parts[1] == "playlist" ? spotifyPlaylist : spotifyAlbum)(parts[2], ct); }
            catch (OperationCanceledException) { throw; }
            catch (HttpRequestException) { apiNotice = "Spotify's API was unavailable."; }
        }
        var result = new Playlist { SourceUrl = link.WebUrl.AbsoluteUri, SourceName = "Spotify", IsReadOnlySource = true,
            SpotifyPlaylistId = parts[1] == "playlist" ? parts[2] : null,
            SpotifyImageUrl = api?.ImageUrl, SpotifySnapshotId = api?.SnapshotId };
        if (api is { HasFullAccess: true })
        {
            result.Name = api.Name;
            result.Items = api.Tracks.Select(FromSpotify).ToList();
            if (api.TotalItems > result.Items.Count) result.ImportNotice = $"{api.TotalItems - result.Items.Count} unavailable or non-track items were skipped.";
            return result;
        }
        var html = await fetchPage(SpotifyMatchup.EmbedUrl(parts[1], parts[2]), ct);
        var tracks = SpotifyMatchup.ParseTrackListEmbed(html);
        if (tracks.Count == 0) throw new InvalidOperationException("Spotify did not expose any tracks. Link an account that owns or collaborates on this playlist and try again.");
        result.Name = api?.Name ?? SpotifyMatchup.ParseCollectionName(html) ?? "Spotify playlist";
        result.Items = tracks.Select(t => new PlaylistItem { SpotifyTrackUri = t.Uri, Title = t.Title, Artist = t.Artist,
            DurationSeconds = t.DurationMs / 1000d, AlbumArtUrl = t.ArtworkUrl }).ToList();
        result.ImportNotice = $"{apiNotice} Imported {tracks.Count} tracks visible on Spotify's public page. Long playlists may be incomplete; link an owner or collaborator account for the full API list.".Trim();
        return result;
    }

    internal static PlaylistItem FromSpotify(SpotifyTrackResult track) => new() { SpotifyTrackUri = track.Uri, Title = track.Name,
        Artist = track.Artist, DurationSeconds = track.DurationMs / 1000d, AlbumArtUrl = track.AlbumArtUrl };
    internal static PlaylistItem FromResolved(RemoteAudioResolveResult result) => new() { SourceUrl = result.SourceUrl,
        Title = result.Title, Artist = result.Artist, DurationSeconds = result.Duration.TotalSeconds, AlbumArtUrl = result.ArtworkUrl };

    private static async Task<string> ExtractAsync(string input, CancellationToken ct)
    {
        var executable = YtDlpService.FindExecutable() ?? throw new InvalidOperationException("Install yt-dlp in Settings to import this playlist.");
        var result = await SafeProcessRunner.RunAsync(executable,
            [.. YtDlpService.JsRuntimeArguments(), "--flat-playlist", "--dump-single-json", "--skip-download", "--ignore-errors", "--playlist-end", "10001", "--", input],
            TimeSpan.FromMinutes(2), maxOutputBytes: 16 * 1024 * 1024, cancellationToken: ct);
        ct.ThrowIfCancellationRequested();
        if (!result.Succeeded && string.IsNullOrWhiteSpace(result.Stdout))
            throw new InvalidOperationException("This source did not expose a playlist. You can still add its individual track links to a playlist.");
        return result.Stdout;
    }

    internal static Playlist ParseExtractorPlaylist(string json, string source)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var result = new Playlist { Name = Text(root, "title") ?? "Imported playlist", SourceUrl = source,
            SourceName = Text(root, "extractor_key") ?? "Web", IsReadOnlySource = true };
        if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("That link did not contain a playlist.");
        if (entries.GetArrayLength() > 10000) throw new InvalidOperationException("This playlist is too large to import in one operation (maximum 10,000 tracks).");
        var skipped = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            var url = Text(entry, "webpage_url") ?? Text(entry, "original_url") ?? Text(entry, "url");
            if (url is not null && !url.Contains("://") && (Text(entry, "ie_key")?.Contains("Youtube", StringComparison.OrdinalIgnoreCase) == true || source.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)))
                url = "https://www.youtube.com/watch?v=" + Uri.EscapeDataString(url);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) { skipped++; continue; }
            result.Items.Add(new PlaylistItem { SourceUrl = uri.AbsoluteUri, Title = Text(entry, "title"),
                Artist = Text(entry, "artist") ?? Text(entry, "uploader") ?? Text(entry, "channel"),
                DurationSeconds = entry.TryGetProperty("duration", out var duration) && duration.ValueKind == JsonValueKind.Number && duration.TryGetDouble(out var seconds) ? seconds : 0 });
        }
        if (result.Items.Count == 0) throw new InvalidOperationException("No available tracks were found in this playlist.");
        if (skipped > 0) result.ImportNotice = $"Skipped {skipped} unavailable entries.";
        return result;
    }
    private static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
}
