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

    private readonly string _cacheRoot;

    public SpotifyMatchupService(string? cacheRoot = null)
    {
        _cacheRoot = cacheRoot ?? DefaultCacheRoot;
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
    /// with confidence (the caller then falls back to the Spotify player). <paramref name="known"/>
    /// is used when the embed page can't be read, e.g. title and artist from the playlist row.
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

        var cached = await FindCachedAsync(trackId, ct);
        if (cached is not null)
        {
            Log($"cache hit {trackId} -> {cached.VideoId}");
            return ToResult(cached);
        }

        var executable = YtDlpService.FindExecutable();
        if (string.IsNullOrWhiteSpace(executable))
        {
            Log("yt-dlp not found; matchup unavailable");
            return null;
        }

        status?.Invoke("Looking up the track...");
        var facts = await FetchFactsAsync(trackId, ct) ?? known;
        if (facts is null || facts.Title.Length == 0)
        {
            Log($"no track details for {trackId}");
            return null;
        }

        status?.Invoke($"Finding \"{facts.Title}\" on YouTube...");
        var match = await FindMatchAsync(executable, facts, ct);
        if (match is null)
        {
            Log($"no confident match for {trackId} \"{facts.Artist} - {facts.Title}\"");
            return null;
        }

        Log($"matched {trackId} \"{facts.Artist} - {facts.Title}\" -> {match.Id} \"{match.Title}\" [{match.Channel}] score={SpotifyMatchup.Score(facts, match):0.00}");
        var fileName = await DownloadAsync(executable, trackId, match, status, ct);
        var entry = new CachedMatch(
            trackId, match.Id, fileName, facts.Title, facts.Artist, facts.Album, facts.ArtworkUrl,
            facts.DurationMs > 0 ? facts.DurationMs : (int)(match.DurationSeconds * 1000), DateTime.UtcNow);
        await RememberAsync(entry, ct);
        return ToResult(entry);
    }

    /// <summary>
    /// The tracks of an album or playlist link, in order, read from its embed page. Empty when the
    /// page can't be read; a long playlist only yields what the embed page shows.
    /// </summary>
    public async Task<IReadOnlyList<SpotifyTrackFacts>> FetchContextTracksAsync(SpotifyPlaybackLink link, CancellationToken ct)
    {
        var parts = link.PlaybackUri.Split(':');
        if (parts.Length != 3 || parts[1] is not ("album" or "playlist"))
        {
            return [];
        }

        try
        {
            var html = await Http.GetStringAsync(SpotifyMatchup.EmbedUrl(parts[1], parts[2]), ct);
            return SpotifyMatchup.ParseTrackListEmbed(html);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log($"track list lookup failed for {link.PlaybackUri}: {ex.Message}");
            return [];
        }
    }

    private static async Task<SpotifyTrackFacts?> FetchFactsAsync(string trackId, CancellationToken ct)
    {
        try
        {
            var html = await Http.GetStringAsync(SpotifyMatchup.EmbedUrl("track", trackId), ct);
            return SpotifyMatchup.ParseTrackEmbed(html);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Log($"track lookup failed for {trackId}: {ex.Message}");
            return null;
        }
    }

    private static async Task<YouTubeCandidate?> FindMatchAsync(string executable, SpotifyTrackFacts facts, CancellationToken ct)
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
            var best = SpotifyMatchup.PickBest(facts, candidates);
            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    private async Task<string> DownloadAsync(
        string executable, string trackId, YouTubeCandidate match, Action<string>? status, CancellationToken ct)
    {
        Directory.CreateDirectory(_cacheRoot);
        // The track id is 22 letters and digits (checked on the way in), so it is a safe file name.
        var template = Path.Combine(_cacheRoot, trackId + ".%(ext)s");
        foreach (var stale in Directory.EnumerateFiles(_cacheRoot, trackId + ".*"))
        {
            TryDeleteFile(stale);
        }

        status?.Invoke("Caching audio...");
        var result = await SafeProcessRunner.RunWithProgressAsync(
            executable,
            [
                .. YtDlpService.JsRuntimeArguments(),
                "--no-playlist",
                "--progress",
                "--newline",
                "--force-overwrites",
                "--no-part",
                "-f",
                "bestaudio[ext=m4a]/bestaudio[ext=mp3]/bestaudio[ext=webm]/bestaudio",
                "-o",
                template,
                "--",
                match.Url,
            ],
            DownloadTimeout,
            onStdoutLine: line =>
            {
                if (OpenUrlService.TryParseYtDlpProgress(line, out var percent))
                {
                    status?.Invoke($"Caching audio... {percent}%");
                }
            },
            maxOutputBytes: 2 * 1024 * 1024,
            ct);

        ct.ThrowIfCancellationRequested();
        if (result.TimedOut)
        {
            throw new InvalidOperationException("yt-dlp timed out downloading the audio.");
        }

        if (result.ExitCode != 0)
        {
            Log($"download failed for {match.Id}: {result.Stderr.Trim()}");
            throw new InvalidOperationException(YtDlpService.DescribeFailure(result.Stderr, result.ExitCode));
        }

        var file = Directory.EnumerateFiles(_cacheRoot, trackId + ".*")
            .Where(static f => !f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static f => new FileInfo(f).Length)
            .FirstOrDefault();
        if (file is null || !Spectralis.Core.Common.SupportedAudioFormats.IsSupportedExtension(file))
        {
            TryDeleteFile(file);
            throw new InvalidOperationException("yt-dlp did not produce a playable audio file.");
        }

        return Path.GetFileName(file);
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

            if (!File.Exists(Path.Combine(_cacheRoot, match.FileName)))
            {
                index.Remove(trackId);
                WriteIndex(index);
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
                    .Where(static e => e is not null && SpotifyMatchup.IsTrackId(e.TrackId) &&
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
