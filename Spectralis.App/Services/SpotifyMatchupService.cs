using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Spectralis.Core.Integrations;
using Spectralis.Core.Integrations.Spotify;

namespace Spectralis.App.Services;

/// <summary>
/// Spotify matchup (beta): turns a Spotify track into a local audio file by finding the same
/// recording on YouTube and downloading it with yt-dlp. Nothing here talks to the Spotify Web API
/// or the playback SDK, so it needs no linked account and no Premium; track details come from
/// Spotify's public embed page and lyrics still come from the Spotify lyrics service.
///
/// Matches and their audio are kept on disk (trimmed to a size cap, least recently played first),
/// so a track is searched for and downloaded once. The rules for what counts as a match live in
/// <see cref="SpotifyMatchup"/>; this class is the network, the process and the cache around them.
/// </summary>
public sealed class SpotifyMatchupService
{
    /// <summary>How much matched audio to keep. Around 250 songs at YouTube's audio bitrate.</summary>
    public const long CacheLimitBytes = 1024L * 1024 * 1024;

    private static readonly TimeSpan SearchTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly SemaphoreSlim IndexLock = new(1, 1);

    public static SpotifyMatchupService Shared { get; } = new();
    private static readonly SemaphoreSlim[] ResolveGates = Enumerable.Range(0, 64).Select(static _ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly ConcurrentDictionary<string, SpotifyTrackFacts> _knownFacts = new();
    private readonly HttpClient _http;
    private readonly string _cacheRoot;
    private readonly Func<string?> _findExecutable;
    private readonly Func<string, CancellationToken, Task<SpotifyTrackFacts?>> _fetchFacts;
    private readonly Func<string, SpotifyTrackFacts, CancellationToken, IAsyncEnumerable<YouTubeCandidate>> _findMatches;
    private readonly Func<string, string, YouTubeCandidate, Action<string>?, CancellationToken, Task<string>> _download;

    public SpotifyMatchupService(string? cacheRoot = null) : this(cacheRoot, null, null, null, null) { }

    internal SpotifyMatchupService(string? cacheRoot,
        Func<string?>? findExecutable,
        Func<string, CancellationToken, Task<SpotifyTrackFacts?>>? fetchFacts,
        Func<string, SpotifyTrackFacts, CancellationToken, Task<YouTubeCandidate?>>? findMatch,
        Func<string, string, YouTubeCandidate, Action<string>?, CancellationToken, Task<string>>? download,
        HttpClient? http = null,
        Func<string, SpotifyTrackFacts, CancellationToken, IAsyncEnumerable<YouTubeCandidate>>? findMatches = null)
    {
        _cacheRoot = Path.GetFullPath(cacheRoot ?? DefaultCacheRoot);
        _http = http ?? Http;
        _findExecutable = findExecutable ?? YtDlpService.FindExecutable;
        _fetchFacts = fetchFacts ?? FetchFactsAsync;
        _findMatches = findMatches ?? (findMatch is null ? FindMatchesAsync : OneMatchAsync);
        _download = download ?? DownloadAsync;

        async IAsyncEnumerable<YouTubeCandidate> OneMatchAsync(string executable, SpotifyTrackFacts facts,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var match = await findMatch!(executable, facts, ct);
            if (match is not null) yield return match;
        }
    }

    public static string DefaultCacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Spectralis",
        "spotify-matchup");

    public static string LogPath => AppLogPaths.For("spotify-matchup.log");

    private string IndexPath => Path.Combine(_cacheRoot, "index.json");

    /// <summary>One remembered match: which upload a track maps to and where its audio is.</summary>
    public sealed record CachedMatch(
        string TrackId,
        string VideoId,
        string FileName,
        string Title,
        string Artist,
        string? Album,
        string? ArtworkUrl,
        int DurationMs,
        DateTime LastPlayedUtc);

    /// <summary>
    /// Resolves a Spotify track to playable local audio, or null when no upload could be matched
    /// with confidence. Complete <paramref name="known"/> facts skip the embed lookup;
    /// partial facts cover a page that can't be read. The beta caller reports a failed match.
    /// </summary>
    public async Task<RemoteAudioResolveResult?> ResolveAsync(
        string trackId,
        SpotifyTrackFacts? known,
        Action<string>? status,
        CancellationToken ct)
    {
        if (!SpotifyMatchup.IsTrackId(trackId))
        {
            return null;
        }

        // Fixed stripes bound lock memory while preventing two decks / Now Playing from
        // racing over the same track's files. Cancellation only affects this caller's work.
        var gate = ResolveGates[GateKey(trackId)];
        await gate.WaitAsync(ct);
        var elapsed = Stopwatch.StartNew();
        try
        {
            return await ResolveCoreAsync(trackId, known, status, ct);
        }
        finally
        {
            gate.Release();
            Log($"resolve {trackId} finished in {elapsed.ElapsedMilliseconds} ms");
        }
    }

    private bool HasAudio(CachedMatch match)
    {
        try { return new FileInfo(Path.Combine(_cacheRoot, match.FileName)).Length > 0; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private int GateKey(string trackId)
    {
        var root = OperatingSystem.IsWindows() ? _cacheRoot.ToUpperInvariant() : _cacheRoot;
        return (int)((uint)StringComparer.Ordinal.GetHashCode(root + "/" + trackId) % (uint)ResolveGates.Length);
    }

    private async Task<RemoteAudioResolveResult?> ResolveCoreAsync(string trackId, SpotifyTrackFacts? known,
        Action<string>? status, CancellationToken ct)
    {
        var cached = await FindCachedAsync(trackId, ct);
        if (cached is not null && HasAudio(cached))
        {
            Log($"cache hit {trackId} -> {cached.VideoId}");
            return ToResult(cached);
        }

        var executable = _findExecutable();
        if (string.IsNullOrWhiteSpace(executable))
        {
            Log("yt-dlp not found; matchup unavailable");
            return null;
        }

        bool Complete(SpotifyTrackFacts? value) => value is not null && value.Id == trackId &&
            !string.IsNullOrWhiteSpace(value.Title) && !string.IsNullOrWhiteSpace(value.Artist) && value.DurationMs > 0;
        var facts = Complete(known) ? known : null;
        if (facts is null && _knownFacts.TryGetValue(trackId, out var remembered) && Complete(remembered)) facts = remembered;
        if (facts is null && cached is not null)
            facts = new(trackId, cached.Title, cached.Artist, cached.DurationMs, cached.ArtworkUrl, cached.Album);
        if (!Complete(facts))
        {
            status?.Invoke("Looking up the track...");
            facts = await _fetchFacts(trackId, ct) ?? facts ?? (known?.Id == trackId ? known : null);
        }
        ct.ThrowIfCancellationRequested();
        if (facts is null || facts.Id != trackId || string.IsNullOrWhiteSpace(facts.Title) || string.IsNullOrWhiteSpace(facts.Artist))
        {
            Log($"no track details for {trackId}");
            return null;
        }

        var attempted = new HashSet<string>(StringComparer.Ordinal);
        Exception? lastDownloadError = null;
        // Audio eviction shouldn't forget an already verified match. Try it once before searching.
        if (cached is not null)
        {
            attempted.Add(cached.VideoId);
            try
            {
                status?.Invoke("Loading the saved YouTube match...");
                return await CacheMatchAsync(executable, facts,
                    new(cached.VideoId, cached.Title, cached.Artist, cached.DurationMs / 1000d), status, ct);
            }
            catch (InvalidOperationException ex) when (!ct.IsCancellationRequested)
            {
                lastDownloadError = ex;
                Log($"saved match {cached.VideoId} unavailable, searching again: {ex.Message}");
            }
        }

        status?.Invoke($"Finding \"{facts.Title}\" on YouTube...");
        await foreach (var match in _findMatches(executable, facts, ct).WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();
            if (!attempted.Add(match.Id)) continue;
            Log($"matched {trackId} -> {match.Id} score={SpotifyMatchup.Score(facts, match):0.00}");
            try
            {
                return await CacheMatchAsync(executable, facts, match, status, ct);
            }
            catch (InvalidOperationException ex) when (!ct.IsCancellationRequested)
            {
                lastDownloadError = ex;
                Log($"match {match.Id} unavailable: {ex.Message}");
                if (attempted.Count >= 3) break;
                status?.Invoke("Trying another matching upload...");
            }
        }
        ct.ThrowIfCancellationRequested();
        if (lastDownloadError is not null)
            throw new InvalidOperationException($"Couldn't download a playable match. {lastDownloadError.Message}", lastDownloadError);
        Log($"no confident match for {trackId} \"{facts.Artist} - {facts.Title}\"");
        return null;
    }

    private async Task<RemoteAudioResolveResult> CacheMatchAsync(string executable, SpotifyTrackFacts facts,
        YouTubeCandidate match, Action<string>? status, CancellationToken ct)
    {
        var fileName = await _download(executable, facts.Id, match, status, ct);
        ct.ThrowIfCancellationRequested();
        var entry = new CachedMatch(facts.Id, match.Id, fileName, facts.Title, facts.Artist, facts.Album, facts.ArtworkUrl,
            facts.DurationMs > 0 ? facts.DurationMs : (int)(match.DurationSeconds * 1000), DateTime.UtcNow);
        await RememberAsync(entry, ct);
        return ToResult(entry);
    }

    /// <summary>
    /// The tracks of an album or playlist link, in order, read from its embed page. Empty when the
    /// page can't be read; a long playlist only yields what the embed page shows.
    /// </summary>
    public void RememberTrackFacts(IEnumerable<SpotifyTrackFacts> tracks)
    {
        if (_knownFacts.Count > 10000) _knownFacts.Clear();
        foreach (var track in tracks.Take(10000)) _knownFacts[track.Id] = track;
    }

    public async Task<IReadOnlyList<SpotifyTrackFacts>> FetchContextTracksAsync(SpotifyPlaybackLink link, CancellationToken ct)
    {
        var parts = link.PlaybackUri.Split(':');
        if (parts.Length != 3 || parts[1] is not ("album" or "playlist"))
        {
            return [];
        }

        try
        {
            var html = await _http.GetStringAsync(SpotifyMatchup.EmbedUrl(parts[1], parts[2]), ct);
            var tracks = SpotifyMatchup.ParseTrackListEmbed(html);
            if (_knownFacts.Count > 1000) _knownFacts.Clear();
            foreach (var track in tracks.Take(1000)) _knownFacts[track.Id] = track;
            return tracks;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log($"track list lookup failed for {link.PlaybackUri}: {ex.Message}");
            return [];
        }
    }

    private async Task<SpotifyTrackFacts?> FetchFactsAsync(string trackId, CancellationToken ct)
    {
        try
        {
            var html = await _http.GetStringAsync(SpotifyMatchup.EmbedUrl("track", trackId), ct);
            return SpotifyMatchup.ParseTrackEmbed(html);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log($"track lookup failed for {trackId}: {ex.Message}");
            return null;
        }
    }

    private static async IAsyncEnumerable<YouTubeCandidate> FindMatchesAsync(string executable, SpotifyTrackFacts facts,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var query in SpotifyMatchup.BuildQueries(facts))
        {
            var result = await SafeProcessRunner.RunAsync(
                executable,
                YtDlpService.BuildCandidateSearchArguments(query, 6),
                SearchTimeout,
                maxOutputBytes: 2 * 1024 * 1024,
                cancellationToken: ct);
            ct.ThrowIfCancellationRequested();
            if (result.TimedOut || result.ExitCode != 0)
            {
                Log($"search \"{query}\" failed: {YtDlpService.DescribeFailure(result.Stderr, result.ExitCode)}");
                continue;
            }

            var candidates = YtDlpService.ParseCandidateSearch(result.Stdout)
                .Select(static c => new YouTubeCandidate(c.Id, c.Title, c.Channel, c.DurationSeconds));
            // Search results can contain private, restricted or removed uploads. Keep
            // the same matching threshold and only try the next query if these fail.
            foreach (var candidate in candidates.OrderByDescending(c => SpotifyMatchup.Score(facts, c)))
                if (SpotifyMatchup.Score(facts, candidate) >= SpotifyMatchup.AcceptScore)
                    yield return candidate;
        }
    }

    private async Task<string> DownloadAsync(
        string executable, string trackId, YouTubeCandidate match, Action<string>? status, CancellationToken ct)
    {
        Directory.CreateDirectory(_cacheRoot);
        var jobRoot = Path.Combine(_cacheRoot, ".download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(jobRoot);
        try
        {
            using var lease = new FileStream(Path.Combine(jobRoot, ".active"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var template = Path.Combine(jobRoot, trackId + ".%(ext)s");
            status?.Invoke("Caching audio...");
            var result = await SafeProcessRunner.RunWithProgressAsync(
                executable,
                [
                    .. YtDlpService.JsRuntimeArguments(),
                    "--ignore-config",
                    "--no-playlist",
                    "--progress",
                    "--newline",
                    "-f",
                    "bestaudio[ext=m4a]/bestaudio[ext=mp3]/bestaudio[ext=webm]/bestaudio",
                    "-o", template,
                    "--", match.Url,
                ],
                DownloadTimeout,
                onStdoutLine: line =>
                {
                    if (OpenUrlService.TryParseYtDlpProgress(line, out var percent))
                        status?.Invoke($"Caching audio... {percent}%");
                },
                maxOutputBytes: 2 * 1024 * 1024,
                ct);
            ct.ThrowIfCancellationRequested();
            if (result.TimedOut) throw new InvalidOperationException("yt-dlp timed out downloading the audio.");
            if (result.ExitCode != 0)
            {
                Log($"download failed for {match.Id}: {result.Stderr.Trim()}");
                throw new InvalidOperationException(YtDlpService.DescribeFailure(result.Stderr, result.ExitCode));
            }

            var file = Directory.EnumerateFiles(jobRoot, trackId + ".*")
                .Where(static f => Spectralis.Core.Common.SupportedAudioFormats.IsSupportedExtension(f) && new FileInfo(f).Length > 0)
                .OrderByDescending(static f => new FileInfo(f).Length).FirstOrDefault();
            if (file is null) throw new InvalidOperationException("yt-dlp did not produce a playable audio file.");

            // Only completed files enter the reusable cache. An interrupted download must
            // never look like a cache hit on the next attempt.
            var fileName = Path.GetFileName(file);
            File.Move(file, Path.Combine(_cacheRoot, fileName), overwrite: true);
            return fileName;
        }
        finally
        {
            TempStorageCleanup.DeleteOwnedTree(_cacheRoot, jobRoot);
        }
    }

    private RemoteAudioResolveResult ToResult(CachedMatch match)
    {
        var path = Path.Combine(_cacheRoot, match.FileName);
        return new RemoteAudioResolveResult(
            RemoteAudioServiceKind.Spotify,
            "Spotify",
            $"https://open.spotify.com/track/{match.TrackId}",
            path,
            Path.GetExtension(path),
            "Spotify matchup",
            match.Title,
            match.Artist,
            match.Album,
            match.ArtworkUrl,
            TimeSpan.FromMilliseconds(match.DurationMs),
            RefererUrl: null,
            CachedAudioPath: path,
            ExternalId: $"spotify:track:{match.TrackId}");
    }

    /// <summary>True for a result this service produced: Spotify by identity, a local file by playback.</summary>
    public static bool IsMatchupResult(RemoteAudioResolveResult result) =>
        result.Kind == RemoteAudioServiceKind.Spotify && !string.IsNullOrWhiteSpace(result.CachedAudioPath);

    // ── the cache ────────────────────────────────────────────────────────────────────────────

    private async Task<CachedMatch?> FindCachedAsync(string trackId, CancellationToken ct)
    {
        await IndexLock.WaitAsync(ct);
        try
        {
            var index = ReadIndex();
            if (!index.TryGetValue(trackId, out var match))
            {
                return null;
            }

            index[trackId] = match with { LastPlayedUtc = DateTime.UtcNow };
            WriteIndex(index);
            return match;
        }
        finally
        {
            IndexLock.Release();
        }
    }

    private async Task RememberAsync(CachedMatch entry, CancellationToken ct)
    {
        await IndexLock.WaitAsync(ct);
        try
        {
            var index = ReadIndex();
            index[entry.TrackId] = entry;
            Trim(index, entry.TrackId);
            WriteIndex(index);
        }
        finally
        {
            IndexLock.Release();
        }
    }

    /// <summary>Drops the least recently played matches until the audio fits the cap. Never the one just added.</summary>
    private void Trim(Dictionary<string, CachedMatch> index, string keepTrackId)
    {
        long Size(CachedMatch m)
        {
            try { return new FileInfo(Path.Combine(_cacheRoot, m.FileName)).Length; }
            catch { return 0; }
        }

        var total = index.Values.Sum(Size);
        foreach (var match in index.Values.Where(m => m.TrackId != keepTrackId).OrderBy(static m => m.LastPlayedUtc).ToList())
        {
            if (total <= CacheLimitBytes)
            {
                break;
            }

            total -= Size(match);
            TryDeleteFile(Path.Combine(_cacheRoot, match.FileName));
            // Keep the small identity mapping so loading an evicted track skips the search.
        }
        foreach (var match in index.Values.Where(m => m.TrackId != keepTrackId)
                     .OrderByDescending(static m => m.LastPlayedUtc).Skip(2047).ToList())
        {
            TryDeleteFile(Path.Combine(_cacheRoot, match.FileName));
            index.Remove(match.TrackId);
        }
    }

    private Dictionary<string, CachedMatch> ReadIndex()
    {
        try
        {
            if (File.Exists(IndexPath))
            {
                var entries = JsonSerializer.Deserialize<List<CachedMatch>>(File.ReadAllText(IndexPath)) ?? [];
                // File names come back off disk, so hold them to the shape this class writes:
                // "<track id>.<ext>", nothing that could point outside the cache folder.
                return entries
                    .Where(static e => e is not null && SpotifyMatchup.IsTrackId(e.TrackId) && IsVideoId(e.VideoId) &&
                                       !string.IsNullOrWhiteSpace(e.Title) && !string.IsNullOrWhiteSpace(e.Artist) &&
                                       !string.IsNullOrEmpty(e.FileName) &&
                                       Spectralis.Core.Common.SupportedAudioFormats.IsSupportedExtension(e.FileName) &&
                                       e.FileName == Path.GetFileName(e.FileName) &&
                                       e.FileName.StartsWith(e.TrackId + ".", StringComparison.Ordinal))
                    .GroupBy(static e => e.TrackId)
                    .ToDictionary(static g => g.Key, static g => g.Last());
            }
        }
        catch (Exception ex)
        {
            Log($"index unreadable, starting fresh: {ex.Message}");
        }

        return [];
    }

    private void WriteIndex(Dictionary<string, CachedMatch> index)
    {
        try
        {
            Directory.CreateDirectory(_cacheRoot);
            var temp = IndexPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(index.Values.ToList()));
            File.Move(temp, IndexPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log($"index not saved: {ex.Message}");
        }
    }

    private static bool IsVideoId(string? id) => id is { Length: 11 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // in use or already gone; the next trim gets it
        }
    }

    private static void Log(string message)
    {
        try { AppLogPaths.AppendTimestamped(LogPath, message); }
        catch { }
    }

    private static HttpClient CreateHttpClient()
    {
        var http = Spectralis.Core.Diagnostics.NetworkClients.Create("spotify-matchup", TimeSpan.FromSeconds(15));
        // The embed page is a browser page; without a browser user agent it answers with a stub.
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
        return http;
    }
}
