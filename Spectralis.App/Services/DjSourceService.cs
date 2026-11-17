using Spectralis.Core.Integrations.Spotify;

namespace Spectralis.App.Services;

public sealed record DjSource(string Reference, string? CachedPath = null, string? Title = null, string? Artist = null, string? Service = null, int DurationMs = 0);

public interface IDjSourceService
{
    Task<DjPreparedSource> PrepareAsync(DjSource source, IProgress<string>? progress, CancellationToken ct);
}

/// <summary>Uses the same URL resolver and cache as Now Playing. Decks decode their
/// own seekable PCM before releasing the download or borrowed cache lease.</summary>
public sealed class DjSourceService : IDjSourceService
{
    public const string SpotifyMessage = "Enable Spotify matchup in Settings > Updates > Beta features to mix Spotify tracks.";
    private readonly Func<string, CancellationToken, Task<RemoteAudioResolveResult>> _resolve;
    private readonly Func<RemoteAudioResolveResult, IProgress<string>?, CancellationToken, Task<string>> _download;
    private readonly Action<string> _release;
    private readonly Func<bool> _matchupEnabled;
    private readonly Func<string, SpotifyTrackFacts?, Action<string>?, CancellationToken, Task<RemoteAudioResolveResult?>> _matchup;

    public DjSourceService(
        Func<string, CancellationToken, Task<RemoteAudioResolveResult>>? resolve = null,
        Func<RemoteAudioResolveResult, IProgress<string>?, CancellationToken, Task<string>>? download = null,
        Action<string>? release = null,
        Func<bool>? matchupEnabled = null,
        Func<string, SpotifyTrackFacts?, Action<string>?, CancellationToken, Task<RemoteAudioResolveResult?>>? matchup = null)
    {
        _resolve = resolve ?? ((url, ct) => new OpenUrlService().ResolveAsync(url, ct));
        _download = download ?? DownloadAsync;
        _release = release ?? (path => RemoteAudioCache.TryDelete(path));
        _matchupEnabled = matchupEnabled ?? (() => BetaFeatures.SpotifyMatchup);
        _matchup = matchup ?? SpotifyMatchupService.Shared.ResolveAsync;
    }

    public static bool IsSpotify(string reference) => reference.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase) || SpotifyPlaybackLink.TryParse(reference, out _) ||
        (Uri.TryCreate(reference.Contains("://") ? reference : "https://" + reference, UriKind.Absolute, out var uri) &&
         (uri.Host.Equals("spotify.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".spotify.com", StringComparison.OrdinalIgnoreCase) ||
          uri.Host.Equals("spotify.link", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("spoti.fi", StringComparison.OrdinalIgnoreCase)));

    public async Task<DjPreparedSource> PrepareAsync(DjSource source, IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (IsSpotify(source.Reference) || source.Service?.Equals("Spotify", StringComparison.OrdinalIgnoreCase) == true)
            return await PrepareSpotifyAsync(source, progress, ct);
        if (source.CachedPath is { } cached && File.Exists(cached))
            return new(cached, source.Reference, source.Title, source.Artist, source.Service ?? "Now Playing");
        if (File.Exists(source.Reference))
            return new(source.Reference, source.Reference, source.Title, source.Artist, source.Service ?? "Library");
        if (!OpenUrlService.MightBeUrl(source.Reference)) throw new FileNotFoundException("Audio source is no longer available.", source.Reference);

        string? ownedPath = null;
        string? jobRoot = null;
        try
        {
            progress?.Report("Resolving URL…");
            var resolved = await _resolve(source.Reference, ct);
            if (resolved.Kind == RemoteAudioServiceKind.Spotify || IsSpotify(resolved.SourceUrl))
            {
                // Matchup files belong to its persistent cache, never this deck's temporary downloads.
                ct.ThrowIfCancellationRequested();
                return await PrepareSpotifyAsync(source with { Reference = resolved.ExternalId ?? resolved.SourceUrl,
                    CachedPath = resolved.CachedAudioPath, Title = resolved.Title ?? source.Title,
                    Artist = resolved.Artist ?? source.Artist }, progress, ct);
            }
            ownedPath = resolved.CachedAudioPath;
            ct.ThrowIfCancellationRequested();
            if (resolved.MetadataOnly) throw new InvalidOperationException("The source returned metadata without playable audio. Try opening the full track link.");
            if (string.IsNullOrWhiteSpace(ownedPath))
            {
                if (resolved.IsWebViewFallback())
                {
                    // A website player cannot supply independent deck audio. Ask
                    // the existing download service for the underlying audio.
                    progress?.Report($"Preparing {resolved.ServiceLabel} audio for mixing…");
                    jobRoot = Path.Combine(Path.GetTempPath(), "Spectralis", "dj-download", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(jobRoot);
                    using var lease = new FileStream(Path.Combine(jobRoot, ".active"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    ownedPath = await SongDownloadService.DownloadFromUrlAsync(resolved.SourceUrl, jobRoot, false,
                        resolved.Title, resolved.Artist, resolved.Album, null, ct);
                }
                else ownedPath = await _download(resolved, progress, ct);
            }
            ct.ThrowIfCancellationRequested();
            var rootToRelease = jobRoot;
            var prepared = new DjPreparedSource(ownedPath, resolved.SourceUrl,
                resolved.Title ?? source.Title, resolved.Artist ?? source.Artist, resolved.ServiceLabel,
                path => { if (rootToRelease is null) _release(path); else TempStorageCleanup.DeleteOwnedTree(Path.GetDirectoryName(rootToRelease)!, rootToRelease); });
            ownedPath = null; jobRoot = null;
            return prepared;
        }
        catch
        {
            if (ownedPath is not null) _release(ownedPath);
            if (jobRoot is not null) TempStorageCleanup.DeleteOwnedTree(Path.GetDirectoryName(jobRoot)!, jobRoot);
            throw;
        }
    }

    private async Task<DjPreparedSource> PrepareSpotifyAsync(DjSource source, IProgress<string>? progress, CancellationToken ct)
    {
        if (!_matchupEnabled()) throw new NotSupportedException(SpotifyMessage);
        var reference = source.Reference;
        if (!SpotifyPlaybackLink.TryParse(reference, out var link))
        {
            if (reference.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Choose a Spotify track link for this deck.");
            progress?.Report("Opening Spotify link...");
            var expanded = await _resolve(reference, ct);
            if (!SpotifyPlaybackLink.TryParse(expanded.ExternalId ?? expanded.SourceUrl, out link))
                throw new NotSupportedException("Choose a Spotify track link for this deck.");
        }
        var trackId = SpotifyMatchup.TrackIdFromUri(link.PlaybackUri);
        if (trackId is null) throw new NotSupportedException("Choose one Spotify track for this deck, rather than an album or playlist.");
        ct.ThrowIfCancellationRequested();
        if (!_matchupEnabled()) throw new NotSupportedException(SpotifyMessage);
        const string label = "Spotify / YouTube match";
        if (source.CachedPath is { } cached && File.Exists(cached))
            return new(cached, link.WebUrl.AbsoluteUri, source.Title, source.Artist, label);
        var known = !string.IsNullOrWhiteSpace(source.Title) && !string.IsNullOrWhiteSpace(source.Artist)
            ? new SpotifyTrackFacts(trackId, source.Title, source.Artist, source.DurationMs) : null;
        var matched = await _matchup(trackId, known, message => progress?.Report(message), ct);
        ct.ThrowIfCancellationRequested();
        if (!_matchupEnabled()) throw new NotSupportedException(SpotifyMessage);
        if (matched?.CachedAudioPath is not { } path || !File.Exists(path))
            throw new InvalidOperationException("No matching YouTube audio found. Try another track or load an audio file.");
        return new(path, link.WebUrl.AbsoluteUri, matched.Title ?? source.Title, matched.Artist ?? source.Artist, label);
    }

    private static Task<string> DownloadAsync(RemoteAudioResolveResult result, IProgress<string>? progress, CancellationToken ct) =>
        RemoteAudioCache.DownloadAsync(result.AudioUrl, result.DownloadExtension, ct, requestInitialRange: true,
            referer: result.RefererUrl ?? result.SourceUrl,
            progress: new Progress<int>(percent => progress?.Report($"Caching {result.ServiceLabel} audio… {percent}%")));
}

public sealed class DjPreparedSource : IDisposable
{
    private readonly FileStream _lease;
    private Action<string>? _release;
    public string Path { get; }
    public string Identity { get; }
    public string? Title { get; }
    public string? Artist { get; }
    public string Service { get; }
    public DjPreparedSource(string path, string identity, string? title, string? artist, string service, Action<string>? release = null)
    {
        Path = path; Identity = identity; Title = title; Artist = artist; Service = service; _release = release;
        _lease = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
    public void Dispose() { _lease.Dispose(); Interlocked.Exchange(ref _release, null)?.Invoke(Path); }
}
