namespace Spectralis;

internal sealed class SharedPlaySessionController : IDisposable
{
    private static readonly TimeSpan TickPublishInterval = TimeSpan.FromSeconds(2);
    private static readonly string LogPath = AppLogPaths.For("shared-play.log");

    private readonly SharedPlayCacheStore cacheStore;
    private readonly SharedPlayCdnClient cdnClient;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly object statusLock = new();

    private CancellationTokenSource cancellation = new();
    private Uri cdnBaseUri = new(SharedPlayDefaults.CdnBaseUrl);
    private SharedPlaySession? session;
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
                    session?.SessionId,
                    session?.JoinUrl,
                    session?.TrackId,
                    liveChannelUrl,
                    lastError);
            }
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        CancellationTokenSource? oldCancellation = null;
        lock (statusLock)
        {
            enabled = settings.EnableSharedPlay;
            cdnBaseUri = new Uri($"{SharedPlayDefaults.NormalizeCdnBaseUrl(settings.SharedPlayCdnBaseUrl)}/");
            liveChannelEnabled = settings.EnableSharedPlayLiveChannel;
            liveChannelId = settings.SharedPlayChannelId;
            liveChannelOwnerToken = settings.SharedPlayChannelOwnerToken;
            liveChannelDisplayName = settings.SharedPlayChannelDisplayName;
            liveChannelUrl = liveChannelEnabled
                ? SharedPlayDefaults.BuildEndpoint(cdnBaseUri, $"/spectralis/web-share/index.html?channel={Uri.EscapeDataString(liveChannelId)}").ToString()
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
        AudioTrackInfo? track,
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
        AudioTrackInfo? track,
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
        AudioTrackInfo track,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        SetUploading(true);
        try
        {
            Log($"Creating session for '{track.DisplayName}' ({track.FilePath}) via {GetCdnBaseUri()}");
            var package = await cacheStore.CreateOrGetPackageAsync(track, cancellationToken);
            Log($"Package ready: {package.PackagePath} ({package.PackageBytes} bytes)");
            var newSession = await cdnClient.CreateSessionAndUploadAsync(cdnBaseUri, package, playback, cancellationToken);
            Log($"Session ready: {newSession.SessionId} {newSession.JoinUrl}");

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
        SharedPlaySession existingSession,
        AudioTrackInfo track,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        SetUploading(true);
        try
        {
            Log($"Adding track '{track.DisplayName}' ({track.FilePath}) to session {existingSession.SessionId}");
            var trackFileKey = CreateTrackFileKey(track);
            if (TryTakePreparedTrack(trackFileKey, out var preparedTrack))
            {
                var activatedSession = await cdnClient.ActivatePreparedTrackAsync(
                    GetCdnBaseUri(),
                    existingSession,
                    preparedTrack,
                    playback,
                    cancellationToken);
                Log($"Prepared session track activated: {activatedSession.SessionId} {activatedSession.TrackId}");

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
            Log($"Session track ready: {updatedSession.SessionId} {updatedSession.TrackId}");

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

    private bool NeedsSessionForTrack(AudioTrackInfo track)
    {
        var current = Snapshot;
        if (current.TrackId is null)
            return true;

        lock (statusLock)
        {
            return !string.Equals(activeTrackFileKey, CreateTrackFileKey(track), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool CanPackageTrack(AudioTrackInfo track)
    {
        try
        {
            return File.Exists(track.FilePath);
        }
        catch
        {
            return false;
        }
    }

    private static bool CanPackagePath(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static string CreateTrackFileKey(AudioTrackInfo track)
    {
        return CreateTrackFileKey(track.FilePath);
    }

    private static string CreateTrackFileKey(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return path;
        }
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
                if (activeSession is null)
                    return;

                lock (statusLock)
                {
                    if (preparedTracks.ContainsKey(fileKey))
                        return;
                }

                Log($"Preparing next queued track '{path}' for session {activeSession.SessionId}");
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
                            .OrderBy(pair => pair.Value.PreparedAtUtc)
                            .Take(preparedTracks.Count - 12)
                            .Select(pair => pair.Key)
                            .ToArray())
                        {
                            preparedTracks.Remove(staleKey);
                        }
                    }
                }

                Log($"Prepared queued track {prepared.TrackId} for session {activeSession.SessionId}");
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
            Log($"Queued track prepare failed: {ex.Message}");
        }
    }

    private async Task PublishLiveChannelAsync(
        SharedPlaySession activeSession,
        AudioTrackInfo track,
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
            track.DisplayName,
            track.Artist,
            track.Album,
            Math.Max(0, track.Duration.TotalSeconds),
            track.FormatName,
            track.Channels,
            track.SourceSampleRate,
            track.BitsPerSample,
            track.AlbumArtBytes is { Length: > 0 },
            track.Lyrics is not null,
            track.EmbeddedVisualizer is not null,
            track.EmbeddedTheme is not null,
            track.EmbeddedHtml is not null || track.EmbeddedMarkdown is not null || track.EmbeddedVideo is not null,
            []);

        var signature = $"{activeSession.SessionId}:{activeSession.TrackId}:{playback.IsPlaying}:{Math.Round(playback.PositionSeconds, 0)}";
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
                activeSession.SessionId,
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
        $"{queue.CurrentIndex}:{string.Join("|", queue.Items.Select(item => $"{item.Id}:{item.TrackId}:{item.PackageUrl}"))}";

    private SharedPlaySession? GetSession()
    {
        lock (statusLock)
        {
            return session;
        }
    }

    private CancellationToken GetCancellationToken()
    {
        lock (statusLock)
        {
            return cancellation.Token;
        }
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
        lock (statusLock)
        {
            isUploading = value;
        }

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
        lock (statusLock)
        {
            return nextRetryUtc;
        }
    }

    private Uri GetCdnBaseUri()
    {
        lock (statusLock)
        {
            return cdnBaseUri;
        }
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
        try { AppLogPaths.AppendTimestamped(LogPath, message); }
        catch { }
    }
}
