using System.Net.Http;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Spectralis.Core.Metadata;
using Spectralis.Core.Playlists;

namespace Spectralis.App.Services;

/// <summary>
/// Resolves a playlist's cover art through a strict fallback chain: a user-uploaded image, then
/// Spotify's own playlist image, then a 2x2 collage built from the first 4 tracks' art, then
/// nothing (the Playlists grid falls back to the app's own placeholder glyph for that last case,
/// same as any other "no art" spot in the app — no generated bitmap needed for it).
/// </summary>
public sealed class PlaylistArtResolver
{
    private const int CollageCellPx = 150;

    private static readonly HttpClient Http = new();

    private readonly Dictionary<string, byte[]?> _urlCache = [];
    private readonly Dictionary<Guid, (Bitmap? Bitmap, string Version)> _resolved = [];

    /// <summary>Cached per playlist Id + a version stamp of everything that could change the
    /// result, so re-rendering the grid doesn't re-fetch/re-decode/re-compose every time.</summary>
    public async Task<Bitmap?> ResolveAsync(Playlist playlist, CancellationToken ct = default)
    {
        var version = BuildVersionStamp(playlist);
        if (_resolved.TryGetValue(playlist.Id, out var cached) && cached.Version == version)
        {
            return cached.Bitmap;
        }

        var directBytes = await ResolveDirectBytesAsync(playlist, ct);
        var bitmap = directBytes is not null
            ? await Dispatcher.UIThread.InvokeAsync(() => TryDecodeBytes(directBytes))
            : await BuildCollageAsync(playlist.Items.Take(4).ToList(), ct);

        // Deliberately not disposing the outgoing bitmap here: Reload() runs on every mutation
        // (including background Spotify syncs), and an older PlaylistRow still mid-layout can be
        // holding the exact same Bitmap reference when this resolves — disposing it out from
        // under a live Image control is a hard crash (Bitmap.get_Size() NRE during Measure), not
        // a graceful "just show nothing." Letting the GC reclaim it once nothing references it
        // anymore is the safe trade for a resource this small and this infrequently replaced.
        _resolved[playlist.Id] = (bitmap, version);
        return bitmap;
    }

    private static string BuildVersionStamp(Playlist playlist) =>
        string.Join('|',
            playlist.CoverImagePath,
            playlist.SpotifyImageUrl,
            string.Join(',', playlist.Items.Take(4).Select(i => $"{i.Path}:{i.SpotifyTrackUri}:{i.AlbumArtUrl}")));

    private async Task<byte[]?> ResolveDirectBytesAsync(Playlist playlist, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(playlist.CoverImagePath) && File.Exists(playlist.CoverImagePath))
        {
            try
            {
                return await File.ReadAllBytesAsync(playlist.CoverImagePath, ct);
            }
            catch
            {
                // Falls through to the next tier below.
            }
        }

        return !string.IsNullOrWhiteSpace(playlist.SpotifyImageUrl)
            ? await FetchUrlAsync(playlist.SpotifyImageUrl, ct)
            : null;
    }

    private async Task<Bitmap?> BuildCollageAsync(List<PlaylistItem> items, CancellationToken ct)
    {
        var byteSets = new List<byte[]>();
        foreach (var item in items)
        {
            var bytes = await ResolveItemArtBytesAsync(item, ct);
            if (bytes is not null)
            {
                byteSets.Add(bytes);
            }
        }

        return byteSets.Count == 0 ? null : await Dispatcher.UIThread.InvokeAsync(() => ComposeCollage(byteSets));
    }

    private async Task<byte[]?> ResolveItemArtBytesAsync(PlaylistItem item, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(item.AlbumArtUrl))
        {
            return await FetchUrlAsync(item.AlbumArtUrl, ct);
        }

        if (!string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path))
        {
            return TrackMetadataReader.Read(item.Path).CoverArt;
        }

        return null;
    }

    private static Bitmap? ComposeCollage(List<byte[]> byteSets)
    {
        var quadrants = byteSets.Select(TryDecodeBytes).Where(b => b is not null).Cast<Bitmap>().ToList();
        if (quadrants.Count == 0)
        {
            return null;
        }

        try
        {
            var target = new RenderTargetBitmap(new PixelSize(CollageCellPx * 2, CollageCellPx * 2), new Vector(96, 96));
            using (var dc = target.CreateDrawingContext())
            {
                for (var i = 0; i < quadrants.Count && i < 4; i++)
                {
                    var quadrant = quadrants[i];
                    var dest = new Rect((i % 2) * CollageCellPx, (i / 2) * CollageCellPx, CollageCellPx, CollageCellPx);
                    dc.DrawImage(quadrant, new Rect(0, 0, quadrant.PixelSize.Width, quadrant.PixelSize.Height), dest);
                }
            }
            return target;
        }
        finally
        {
            foreach (var quadrant in quadrants)
            {
                quadrant.Dispose();
            }
        }
    }

    private async Task<byte[]?> FetchUrlAsync(string url, CancellationToken ct)
    {
        if (_urlCache.TryGetValue(url, out var cached))
        {
            return cached;
        }

        byte[]? bytes;
        try
        {
            bytes = await Http.GetByteArrayAsync(url, ct);
        }
        catch
        {
            bytes = null;
        }

        _urlCache[url] = bytes;
        return bytes;
    }

    private static Bitmap? TryDecodeBytes(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}
