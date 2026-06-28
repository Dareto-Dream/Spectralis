using Spectralis.Core.Common;

namespace Spectralis.Core.SharedPlay;

public sealed class SharedPlaySessionController : IDisposable
{
    private static readonly TimeSpan TickPublishInterval = TimeSpan.FromSeconds(2);

    private static string LogPath =>
        Path.Combine(Path.GetTempPath(), "spectralis", "shared-play.log");

    private readonly SharedPlayCacheStore cacheStore;
    private readonly SharedPlayCdnClient cdnClient;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly object statusLock = new();

    private CancellationTokenSource cancellation = new();
    private Uri cdnBaseUri = new(SharedPlayDefaults.CdnBaseUrl);
    private SharedPlayRoomSession? session;
    private string? activeTrackFileKey;
    private SharedPlayTrackDescriptor? activeTrackDescriptor;
    private bool enabled;
    private bool isUploading;
    private bool liveChannelEnabled;
    private string liveChannelId = "";
    private string liveChannelOwnerToken = "";
    private string liveChannelDisplayName = "Spectralis Listener";
    private string? liveChannelUrl;
    private string? lastError;
    private DateTimeOffset lastTickPublishedUtc = DateTimeOffset.MinValue;
    private DateTimeOffset nextRetryUtc = DateTimeOffset.MinValue;
    private string? lastPublishedSignature;
    private string? lastPublishedQueueSignature;
    private string? lastPublishedChannelSignature;
    private readonly Dictionary<string, SharedPlayPreparedTrack> preparedTracks = new(StringComparer.OrdinalIgnoreCase);

    public SharedPlaySessionController()
        : this(new SharedPlayCacheStore(), new SharedPlayCdnClient())
    {
    }

    internal SharedPlaySessionController(SharedPlayCacheStore cacheStore, SharedPlayCdnClient cdnClient)
    {
        this.cacheStore = cacheStore;
        this.cdnClient = cdnClient;
    }

    public event EventHandler? StatusChanged;

    public SharedPlaySessionSnapshot Snapshot
    {
        get
        {
            lock (statusLock)
            {
                return new SharedPlaySessionSnapshot(
                    enabled,
                    isUploading,
                    session?.RoomCode,
                    session?.DisplayCode,
                    session?.JoinUrl,
                    session?.TrackId,
                    liveChannelUrl,
                    lastError);
            }
        }
    }

    public void ApplySettings(
        bool enableSharedPlay,
        string? cdnBaseUrl,
        bool enableLiveChannel,
        string channelId,
        string channelOwnerToken,
        string channelDisplayName)
    {
        CancellationTokenSource? oldCancellation = null;
        lock (statusLock)
        {
            enabled = enableSharedPlay;
            cdnBaseUri = new Uri($"{SharedPlayDefaults.NormalizeCdnBaseUrl(cdnBaseUrl)}/");
            liveChannelEnabled = enableLiveChannel;
            liveChannelId = channelId;
            liveChannelOwnerToken = channelOwnerToken;
            liveChannelDisplayName = channelDisplayName;
            liveChannelUrl = liveChannelEnabled
                ? SharedPlayDefaults.BuildEndpoint(cdnBaseUri, $"/spectralis/web-share?channel={Uri.EscapeDataString(liveChannelId)}").ToString()
                : null;

            if (!enabled)
            {
                oldCancellation = cancellation;
                cancellation = new CancellationTokenSource();
                session = null;
                activeTrackFileKey = null;
                activeTrackDescriptor = null;
                isUploading = false;
                lastError = null;
                nextRetryUtc = DateTimeOffset.MinValue;
                lastPublishedSignature = null;
                lastPublishedQueueSignature = null;
                lastPublishedChannelSignature = null;
                preparedTracks.Clear();
            }
        }

        if (oldCancellation is not null)
        {
            oldCancellation.Cancel();
            oldCancellation.Dispose();
        }

        OnStatusChanged();
    }

    public void NotifyPlaybackChanged(
        TrackInfo? track,
        bool isPlaying,
        double positionSeconds,
        double durationSeconds,
        string reason,
        SharedPlayQueueSnapshot? queue = null)
    {
        if (!Snapshot.IsEnabled)
            return;

        var playback = new SharedPlayPlaybackSnapshot(
            isPlaying,
            Math.Clamp(positionSeconds, 0, Math.Max(0, durationSeconds)),
            Math.Max(0, durationSeconds),
            string.IsNullOrWhiteSpace(reason) ? "state" : reason,
            DateTimeOffset.UtcNow);

        _ = ProcessPlaybackChangedAsync(track, playback, queue);
    }

    public async Task<SharedPlayQueueSnapshot?> FetchQueueStateAsync(CancellationToken cancellationToken)
    {
        var activeSession = GetSession();
        if (activeSession is null)
            return null;

        return await cdnClient.FetchQueueStateAsync(activeSession.QueueUrl, cancellationToken);
    }

    public string? GetJoinUrl() => Snapshot.JoinUrl;
    public string? GetChannelUrl() => Snapshot.ChannelUrl;
    public string? GetRoomCode() => Snapshot.RoomCode;
    public string? GetDisplayCode() => Snapshot.DisplayCode;
    public Uri GetCdnBaseUriPublic() => GetCdnBaseUri();

    public (string RoomCode, string SessionKey, Uri CdnBaseUri)? GetSessionCredentials()
    {
        lock (statusLock)
        {
            if (session is null || string.IsNullOrEmpty(session.RoomCode))
                return null;
            return (session.RoomCode, session.SessionKey, cdnBaseUri);
        }
    }

    public (string ChannelId, string OwnerToken)? GetChannelCredentials()
    {
        lock (statusLock)
        {
            if (!liveChannelEnabled || string.IsNullOrEmpty(liveChannelId) || string.IsNullOrEmpty(liveChannelOwnerToken))
                return null;
            return (liveChannelId, liveChannelOwnerToken);
        }
    }

    public async Task<StreamerQueueState?> FetchStreamerQueueAsync(CancellationToken cancellationToken)
    {
        var creds = GetSessionCredentials();
        if (creds is null) return null;
        return await cdnClient.GetStreamerQueueAsync(creds.Value.CdnBaseUri, creds.Value.RoomCode, cancellationToken);
    }

    public async Task SaveStreamerQueueSettingsAsync(
        bool enabled, StreamerQueueSettings settings, CancellationToken cancellationToken)
    {
        var creds = GetSessionCredentials();
        if (creds is null) throw new InvalidOperationException("No active session.");
        await cdnClient.PutStreamerQueueSettingsAsync(
            creds.Value.CdnBaseUri, creds.Value.RoomCode, creds.Value.SessionKey,
            enabled, settings, cancellationToken);
    }

    public async Task<string?> GetStripeConnectUrlAsync(CancellationToken cancellationToken)
    {
        var creds = GetSessionCredentials();
        var ch = GetChannelCredentials();
        if (creds is null || ch is null) return null;
        return await cdnClient.GetStripeConnectUrlAsync(
            creds.Value.CdnBaseUri, ch.Value.ChannelId, ch.Value.OwnerToken, cancellationToken);
    }

    public async Task ApproveStreamerQueueItemAsync(string itemId, CancellationToken cancellationToken)
    {
        var creds = GetSessionCredentials();
        if (creds is null) throw new InvalidOperationException("No active session.");
        await cdnClient.ApproveSubmissionAsync(
            creds.Value.CdnBaseUri, creds.Value.RoomCode, creds.Value.SessionKey, itemId, cancellationToken);
    }

    public async Task RejectStreamerQueueItemAsync(string itemId, CancellationToken cancellationToken)
    {
        var creds = GetSessionCredentials();
        if (creds is null) throw new InvalidOperationException("No active session.");
        await cdnClient.RejectSubmissionAsync(
            creds.Value.CdnBaseUri, creds.Value.RoomCode, creds.Value.SessionKey, itemId, cancellationToken);
    }

    public void ClearActiveSession()
    {
        CancellationTokenSource? oldCancellation = null;
        lock (statusLock)
        {
            oldCancellation = cancellation;
            cancellation = new CancellationTokenSource();
            session = null;
            activeTrackFileKey = null;
            activeTrackDescriptor = null;
            isUploading = false;
            lastError = null;
            nextRetryUtc = DateTimeOffset.MinValue;
            lastPublishedSignature = null;
            lastPublishedQueueSignature = null;
            lastPublishedChannelSignature = null;
            lastTickPublishedUtc = DateTimeOffset.MinValue;
            preparedTracks.Clear();
        }

        oldCancellation.Cancel();
        oldCancellation.Dispose();
        OnStatusChanged();
    }

    public void ClearCache() => cacheStore.Clear();

    public void PrepareUpcomingTrack(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Snapshot.IsEnabled)
            return;

        if (!CanPackagePath(path))
            return;

        _ = PrepareUpcomingTrackAsync(path);
    }

    public bool TryGetPreparedTrack(string path, out SharedPlayPreparedTrack preparedTrack)
    {
        lock (statusLock)
        {
            return preparedTracks.TryGetValue(CreateTrackFileKey(path), out preparedTrack!);
        }
    }

    private async Task ProcessPlaybackChangedAsync(
        TrackInfo? track,
        SharedPlayPlaybackSnapshot playback,
        SharedPlayQueueSnapshot? queue)
    {
        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(GetCancellationToken());
            var cancellationToken = linkedCancellation.Token;
            await operationGate.WaitAsync(cancellationToken);
            try
            {
                if (!Snapshot.IsEnabled)
                    return;

                if (track is null)
                {
                    ClearSession();
                    return;
                }

                if (string.Equals(playback.Reason, "tick", StringComparison.OrdinalIgnoreCase) &&
                    DateTimeOffset.UtcNow < GetNextRetryUtc())
                {
                    return;
                }

                if (!playback.IsPlaying && NeedsSessionForTrack(track) && CanPackageTrack(track))
                {
                    if (GetSession() is null)
                        ClearSession();
                    return;
                }

                if (playback.IsPlaying && NeedsSessionForTrack(track) && CanPackageTrack(track))
                {
                    if (GetSession() is { } existingSession)
                        await UpdateSessionTrackAsync(existingSession, track, playback, cancellationToken);
                    else
                        await StartSessionForTrackAsync(track, playback, cancellationToken);
                }

                var activeSession = Snapshot.TrackId is not null ? GetSession() : null;
                if (activeSession is not null && ShouldPublish(playback))
                {
                    await cdnClient.PublishPlaybackStateAsync(activeSession, playback, cancellationToken);
                    MarkPublished(playback);
                    SetError(null);
                }

                if (activeSession is not null && queue is not null && ShouldPublishQueue(queue))
                {
                    await cdnClient.PublishQueueStateAsync(activeSession, queue, cancellationToken);
                    MarkQueuePublished(queue);
                    SetError(null);
                }

                if (activeSession is not null)
                    await PublishLiveChannelAsync(activeSession, track, playback, cancellationToken);
            }
            finally
            {
                operationGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetUploading(false);
            Log($"Playback update failed: {ex}");
            SetError(ex.Message);
        }
    }

    private async Task StartSessionForTrackAsync(
        TrackInfo track,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        SetUploading(true);
        try
        {
            Log($"Creating session for '{track.DisplayTitle}' ({track.SourcePath}) via {GetCdnBaseUri()}");
            var package = await cacheStore.CreateOrGetPackageAsync(track, cancellationToken);
            Log($"Package ready: {package.PackagePath} ({package.PackageBytes} bytes)");
            var newSession = await cdnClient.CreateSessionAndUploadAsync(cdnBaseUri, package, playback, cancellationToken);
            Log($"Session ready: Room {newSession.DisplayCode} — {newSession.JoinUrl}");

            lock (statusLock)
            {
                session = newSession;
                activeTrackFileKey = CreateTrackFileKey(track);
                activeTrackDescriptor = package.Track;
                lastError = null;
                nextRetryUtc = DateTimeOffset.MinValue;
                lastPublishedSignature = null;
                lastPublishedQueueSignature = null;
                lastPublishedChannelSignature = null;
                lastTickPublishedUtc = DateTimeOffset.MinValue;
            }
        }
        finally
        {
            SetUploading(false);
        }

        OnStatusChanged();
    }

    private async Task UpdateSessionTrackAsync(
        SharedPlayRoomSession existingSession,
        TrackInfo track,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        SetUploading(true);
        try
        {
            Log($"Adding track '{track.DisplayTitle}' ({track.SourcePath}) to room {existingSession.DisplayCode}");
            var trackFileKey = CreateTrackFileKey(track);
            if (TryTakePreparedTrack(trackFileKey, out var preparedTrack))
            {
                var activatedSession = await cdnClient.ActivatePreparedTrackAsync(
                    GetCdnBaseUri(),
                    existingSession,
                    preparedTrack,
                    playback,
                    cancellationToken);
                Log($"Prepared track activated: {activatedSession.DisplayCode} → {activatedSession.TrackId}");

                lock (statusLock)
                {
                    session = activatedSession;
                    activeTrackFileKey = trackFileKey;
                    activeTrackDescriptor = preparedTrack.Track;
                    lastError = null;
                    nextRetryUtc = DateTimeOffset.MinValue;
                    lastPublishedSignature = null;
                    lastPublishedQueueSignature = null;
                    lastPublishedChannelSignature = null;
                    lastTickPublishedUtc = DateTimeOffset.MinValue;
                }

                return;
            }

            var package = await cacheStore.CreateOrGetPackageAsync(track, cancellationToken);
            Log($"Package ready: {package.PackagePath} ({package.PackageBytes} bytes)");
            var updatedSession = await cdnClient.UploadTrackToSessionAsync(
                cdnBaseUri,
                existingSession,
                package,
                playback,
                cancellationToken);
            Log($"Session track ready: {updatedSession.DisplayCode} → {updatedSession.TrackId}");

            lock (statusLock)
            {
                session = updatedSession;
                activeTrackFileKey = trackFileKey;
                activeTrackDescriptor = package.Track;
                lastError = null;
                nextRetryUtc = DateTimeOffset.MinValue;
                lastPublishedSignature = null;
                lastPublishedQueueSignature = null;
                lastPublishedChannelSignature = null;
                lastTickPublishedUtc = DateTimeOffset.MinValue;
            }
        }
        finally
        {
            SetUploading(false);
        }

        OnStatusChanged();
    }

    private bool NeedsSessionForTrack(TrackInfo track)
    {
        var current = Snapshot;
        if (current.TrackId is null)
            return true;

        lock (statusLock)
        {
            return !string.Equals(activeTrackFileKey, CreateTrackFileKey(track), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool CanPackageTrack(TrackInfo track)
    {
        try { return File.Exists(track.SourcePath); }
        catch { return false; }
    }

    private static bool CanPackagePath(string path)
    {
        try { return File.Exists(path); }
        catch { return false; }
    }

    private static string CreateTrackFileKey(TrackInfo track) => CreateTrackFileKey(track.SourcePath);

    private static string CreateTrackFileKey(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch { return path; }
    }

    private bool TryTakePreparedTrack(string fileKey, out SharedPlayPreparedTrack preparedTrack)
    {
        lock (statusLock)
        {
            if (preparedTracks.Remove(fileKey, out preparedTrack!))
                return true;
        }
        return false;
    }

    private async Task PrepareUpcomingTrackAsync(string path)
    {
        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(GetCancellationToken());
            var cancellationToken = linkedCancellation.Token;
            var activeSession = GetSession();
            if (activeSession is null)
                return;

            var fileKey = CreateTrackFileKey(path);
            lock (statusLock)
            {
                if (preparedTracks.ContainsKey(fileKey))
                    return;
            }

            await operationGate.WaitAsync(cancellationToken);
            try
            {
                activeSession = GetSession();
                if (activeSession is null) return;

                lock (statusLock)
                {
                    if (preparedTracks.ContainsKey(fileKey)) return;
                }

                Log($"Preparing next queued track '{path}' for room {activeSession.DisplayCode}");
                var package = await cacheStore.CreateOrGetPackageAsync(path, cancellationToken);
                var playback = new SharedPlayPlaybackSnapshot(
                    false,
                    0,
                    Math.Max(0, package.Track.DurationSeconds),
                    "preload",
                    DateTimeOffset.UtcNow,
                    package.TrackId);
                var prepared = await cdnClient.PrepareTrackInSessionAsync(
                    GetCdnBaseUri(),
                    activeSession,
                    fileKey,
                    package,
                    playback,
                    cancellationToken);

                lock (statusLock)
                {
                    preparedTracks[fileKey] = prepared;
                    if (preparedTracks.Count > 12)
                    {
                        foreach (var staleKey in preparedTracks
                            .OrderBy(p => p.Value.PreparedAtUtc)
                            .Take(preparedTracks.Count - 12)
                            .Select(p => p.Key)
                            .ToArray())
                        {
                            preparedTracks.Remove(staleKey);
                        }
                    }
                }

                Log($"Prepared queued track {prepared.TrackId} for room {activeSession.DisplayCode}");
            }
            finally
            {
                operationGate.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log($"Queued track prepare failed: {ex.Message}");
        }
    }

    private async Task PublishLiveChannelAsync(
        SharedPlayRoomSession activeSession,
        TrackInfo track,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        string channelId;
        string ownerToken;
        string displayName;
        bool channelEnabled;
        SharedPlayTrackDescriptor? descriptor;
        lock (statusLock)
        {
            channelEnabled = liveChannelEnabled;
            channelId = liveChannelId;
            ownerToken = liveChannelOwnerToken;
            displayName = liveChannelDisplayName;
            descriptor = activeTrackDescriptor;
        }

        if (!channelEnabled || string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(ownerToken))
            return;

        descriptor ??= new SharedPlayTrackDescriptor(
            track.DisplayTitle,
            track.Artist,
            track.Album,
            Math.Max(0, track.Duration.TotalSeconds),
            track.FormatName,
            track.Channels,
            track.SampleRateHz,
            0,
            track.CoverArt is { Length: > 0 },
            false,
            track.EmbeddedVisualizer is not null,
            track.EmbeddedTheme is not null,
            track.HasEmbeddedContent,
            []);

        var signature = $"{activeSession.RoomCode}:{activeSession.TrackId}:{playback.IsPlaying}:{Math.Round(playback.PositionSeconds, 0)}";
        lock (statusLock)
        {
            if (string.Equals(signature, lastPublishedChannelSignature, StringComparison.Ordinal))
                return;
            lastPublishedChannelSignature = signature;
        }

        var response = await cdnClient.PublishChannelAsync(
            GetCdnBaseUri(),
            channelId,
            new SharedPlayChannelPublishRequest(
                SharedPlayDefaults.ProtocolVersion,
                ownerToken,
                displayName,
                true,
                activeSession.RoomCode,
                activeSession.JoinUrl,
                activeSession.TrackId,
                descriptor,
                playback with { TrackId = activeSession.TrackId }),
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(response?.ChannelUrl))
        {
            lock (statusLock)
            {
                liveChannelUrl = response.ChannelUrl;
            }
        }
    }

    private bool ShouldPublish(SharedPlayPlaybackSnapshot playback)
    {
        if (string.Equals(playback.Reason, "tick", StringComparison.OrdinalIgnoreCase))
        {
            var elapsed = playback.HostClockUtc - lastTickPublishedUtc;
            if (elapsed < TickPublishInterval)
                return false;
        }

        var signature = $"{playback.IsPlaying}:{Math.Round(playback.PositionSeconds, 2)}:{playback.Reason}";
        return !string.Equals(signature, lastPublishedSignature, StringComparison.Ordinal);
    }

    private void MarkPublished(SharedPlayPlaybackSnapshot playback)
    {
        lock (statusLock)
        {
            if (string.Equals(playback.Reason, "tick", StringComparison.OrdinalIgnoreCase))
                lastTickPublishedUtc = playback.HostClockUtc;
            lastPublishedSignature = $"{playback.IsPlaying}:{Math.Round(playback.PositionSeconds, 2)}:{playback.Reason}";
        }
    }

    private bool ShouldPublishQueue(SharedPlayQueueSnapshot queue)
    {
        var signature = BuildQueueSignature(queue);
        lock (statusLock)
        {
            return !string.Equals(signature, lastPublishedQueueSignature, StringComparison.Ordinal);
        }
    }

    private void MarkQueuePublished(SharedPlayQueueSnapshot queue)
    {
        lock (statusLock)
        {
            lastPublishedQueueSignature = BuildQueueSignature(queue);
        }
    }

    private static string BuildQueueSignature(SharedPlayQueueSnapshot queue) =>
        $"{queue.CurrentIndex}:{string.Join("|", queue.Items.Select(i => $"{i.Id}:{i.TrackId}:{i.PackageUrl}"))}";

    private SharedPlayRoomSession? GetSession()
    {
        lock (statusLock) { return session; }
    }

    private CancellationToken GetCancellationToken()
    {
        lock (statusLock) { return cancellation.Token; }
    }

    private void ClearSession()
    {
        lock (statusLock)
        {
            session = null;
            activeTrackFileKey = null;
            isUploading = false;
            lastPublishedSignature = null;
            lastPublishedQueueSignature = null;
            nextRetryUtc = DateTimeOffset.MinValue;
        }

        OnStatusChanged();
    }

    private void SetUploading(bool value)
    {
        lock (statusLock) { isUploading = value; }
        OnStatusChanged();
    }

    private void SetError(string? error)
    {
        lock (statusLock)
        {
            lastError = error;
            nextRetryUtc = error is null
                ? DateTimeOffset.MinValue
                : DateTimeOffset.UtcNow.AddSeconds(30);
        }

        if (!string.IsNullOrWhiteSpace(error))
            Log($"Unavailable: {error}");

        OnStatusChanged();
    }

    private void OnStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    private DateTimeOffset GetNextRetryUtc()
    {
        lock (statusLock) { return nextRetryUtc; }
    }

    private Uri GetCdnBaseUri()
    {
        lock (statusLock) { return cdnBaseUri; }
    }

    public void Dispose()
    {
        cancellation.Cancel();
        cancellation.Dispose();
        operationGate.Dispose();
        cdnClient.Dispose();
    }

    private static void Log(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
