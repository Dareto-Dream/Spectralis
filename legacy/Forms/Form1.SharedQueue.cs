using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Spectralis;

public partial class Form1
{
    private static readonly Uri SharedQueueLofiFallback =
        new("https://www.youtube.com/watch?v=jfKfPfyJRdk");

    private SharedPlayQueueSnapshot BuildSharedPlayQueueSnapshot()
    {
        var items = queue.Items
            .Select((path, index) => CreateSharedQueueItem(path, index))
            .ToArray();

        return new SharedPlayQueueSnapshot(
            1,
            queue.CurrentIndex,
            items,
            DateTimeOffset.UtcNow);
    }

    private SharedPlayQueueItem CreateSharedQueueItem(string pathOrPointer, int index)
    {
        var sourceKind = DetectSharedQueueSourceKind(pathOrPointer);
        var url = IsSharedQueuePointer(pathOrPointer) ? pathOrPointer : null;
        var title = url is not null
            ? BuildSharedQueueUrlTitle(pathOrPointer)
            : Path.GetFileNameWithoutExtension(pathOrPointer);

        if (index == queue.CurrentIndex && GetActiveTrackForUi() is { } activeTrack)
            title = activeTrack.DisplayName;

        var preparedTrackId = default(string);
        var preparedPackageUrl = default(Uri);
        if (url is null && sharedPlay.TryGetPreparedTrack(pathOrPointer, out var preparedTrack))
        {
            preparedTrackId = preparedTrack.TrackId;
            preparedPackageUrl = preparedTrack.PackageUrl;
            title = preparedTrack.Track.DisplayName;
        }

        return new SharedPlayQueueItem(
            BuildSharedQueueItemId(pathOrPointer),
            sourceKind,
            string.IsNullOrWhiteSpace(title) ? "Queued track" : title,
            index == queue.CurrentIndex ? GetActiveTrackForUi()?.Artist : null,
            index == queue.CurrentIndex ? GetActiveTrackForUi()?.Album : null,
            url,
            ExtractSharedQueueSourceId(sourceKind, pathOrPointer),
            null,
            "host",
            DateTimeOffset.UtcNow,
            preparedTrackId,
            preparedPackageUrl);
    }

    private IEnumerable<string> GetUpcomingSharedPlayLocalQueuePaths(int count)
    {
        if (count <= 0 || queue.IsEmpty || queue.CurrentIndex < 0)
            yield break;

        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var offset = 1; offset <= queue.Count && emitted.Count < count; offset++)
        {
            var nextIndex = queue.CurrentIndex + offset;
            if (nextIndex >= queue.Count)
            {
                if (queue.Repeat != RepeatMode.All)
                    yield break;

                nextIndex %= queue.Count;
            }

            if (nextIndex == queue.CurrentIndex)
                yield break;

            var candidate = queue.Items[nextIndex];
            if (IsSharedQueuePointer(candidate) || !File.Exists(candidate) || !emitted.Add(candidate))
                continue;

            yield return candidate;
        }
    }

    private async Task PullSharedQueueAdditionsAsync()
    {
        if (!appSettings.EnableSharedPlay || HasJoinedSharedPlayActivity)
            return;

        var now = Environment.TickCount64;
        if (now < nextSharedPlayQueuePullTick)
            return;

        nextSharedPlayQueuePullTick = now + 3000;

        try
        {
            var remoteQueue = await sharedPlay.FetchQueueStateAsync(CancellationToken.None);
            if (remoteQueue is null || remoteQueue.Items.Length == 0)
                return;

            var existingIds = queue.Items
                .Select(BuildSharedQueueItemId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var additions = remoteQueue.Items
                .Where(item => string.Equals(item.AddedBy, "remote", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Url)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Where(url => !existingIds.Contains(BuildSharedQueueItemId(url!)))
                .Select(url => url!)
                .ToArray();

            if (additions.Length == 0)
                return;

            queue.AddRange(additions);
            SyncQueueControl();
            if (isQueueVisible)
                RefreshContentColumns();
            NotifySharedPlayPlaybackChanged("queue-add");
            UpdateUiState();
        }
        catch
        {
            // Playback sync is more important than queue decoration; retry on the next pulse.
        }
    }

    private async Task PlayQueueItemAsync(string pathOrPointer, bool startPlayback)
    {
        if (!IsSharedQueuePointer(pathOrPointer))
        {
            if (startPlayback && !CheckContentWarning(pathOrPointer))
                return;

            LoadAudioFile(pathOrPointer, startPlayback: startPlayback, fromQueue: true);
            return;
        }

        if (!startPlayback)
            return;

        var expanded = await TryExpandOpenUrlAsync(pathOrPointer, CancellationToken.None);
        if (expanded?.Target == OpenUrlTarget.Spotify)
        {
            await PlaySharedSpotifyPointerWithFallbackAsync(expanded.Url);
            return;
        }

        await OpenDetectedUrlAsync(pathOrPointer);
        NotifySharedPlayPlaybackChanged("queue-pointer");
    }

    private async Task PlaySharedSpotifyPointerWithFallbackAsync(string spotifyPointer)
    {
        if (spotifyService.IsLinked)
        {
            await LoadSpotifyUriAsync(NormalizeSpotifyPlaybackUri(spotifyPointer) ?? spotifyPointer);
            if (IsSpotifyActive)
            {
                NotifySharedPlayPlaybackChanged("queue-spotify");
                return;
            }
        }

        var query = BuildSharedQueueSearchQuery(spotifyPointer);
        var youtubeUrl = await TryFindYouTubeEquivalentAsync(query);
        if (!string.IsNullOrWhiteSpace(youtubeUrl))
        {
            await LoadYouTubeUrlAsync(youtubeUrl);
            NotifySharedPlayPlaybackChanged("queue-youtube-fallback");
            return;
        }

        await PlaySharedQueueFallbackBeatAsync();
    }

    private async Task PlaySharedQueueFallbackBeatAsync()
    {
        await LoadYouTubeUrlAsync(SharedQueueLofiFallback.AbsoluteUri);
        SetYouTubeStatus("This music is not available for this track, so Spectralis is playing a fallback beat.");
        NotifySharedPlayPlaybackChanged("queue-unavailable-fallback");
    }

    private static async Task<string?> TryFindYouTubeEquivalentAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var ytDlp = YtDlpService.FindExecutable();
        if (ytDlp is null)
            return null;

        try
        {
            return await YtDlpService.SearchFirstVideoUrlAsync(ytDlp, query, CancellationToken.None);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildSharedQueueSearchQuery(string pointer)
    {
        if (Uri.TryCreate(pointer, UriKind.Absolute, out var uri))
        {
            var parts = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.UnescapeDataString);
            return string.Join(' ', parts);
        }

        if (pointer.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = pointer.Split(':', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 3 ? $"spotify {parts[1]} {parts[2]}" : pointer;
        }

        return pointer;
    }

    private static bool IsSharedQueuePointer(string value) =>
        IsHttpUrl(value) ||
        value.TrimStart().StartsWith("spotify:", StringComparison.OrdinalIgnoreCase);

    private static string DetectSharedQueueSourceKind(string value)
    {
        if (value.TrimStart().StartsWith("spotify:", StringComparison.OrdinalIgnoreCase) || IsSpotifyInput(value))
            return "spotify";
        if (IsYouTubeInput(value))
            return "youtube";
        if (IsSoundCloudInput(value))
            return "soundcloud";
        if (IsSunoInput(value))
            return "suno";
        if (IsDirectAudioUrl(value))
            return "direct-audio";
        return IsHttpUrl(value) ? "url" : "local-file";
    }

    private static string? ExtractSharedQueueSourceId(string sourceKind, string value) =>
        sourceKind switch
        {
            "youtube" => ExtractYouTubeVideoId(value),
            "spotify" => NormalizeSpotifyPlaybackUri(value),
            _ => null
        };

    private static string BuildSharedQueueUrlTitle(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return value;

        var last = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();
        return string.IsNullOrWhiteSpace(last)
            ? uri.Host
            : Uri.UnescapeDataString(last);
    }

    private static string BuildSharedQueueItemId(string value)
    {
        var normalized = value.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            normalized = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToLowerInvariant()));
        return Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
    }

    // ── Content-warning gate ──────────────────────────────────────────────

    /// <summary>
    /// If the track at <paramref name="path"/> has content warnings, shows the
    /// pre-play popup. Returns <c>true</c> if playback should proceed.
    /// </summary>
    private bool CheckContentWarning(string path)
    {
        var tags = TrackContentWarningStore.Get(path);
        if (tags.Length == 0)
            return true;

        var trackName = System.IO.Path.GetFileNameWithoutExtension(path);
        using var dlg = new ContentWarningDialog(tags, trackName, themePalette);
        return dlg.ShowDialog(this) == DialogResult.OK;
    }
}
