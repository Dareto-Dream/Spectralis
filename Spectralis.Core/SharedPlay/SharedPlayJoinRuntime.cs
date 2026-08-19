using Spectralis.Core.Common;
using Spectralis.Core.Metadata;

namespace Spectralis.Core.SharedPlay;

/// <summary>
/// Runtime for joining someone else's Shared Play room as a listener. Ported from the legacy
/// WinForms join flow (Form1.SharedPlayJoin.cs). Follows the same decoupling
/// <see cref="Spectralis.Core.Capsule.AlbumWorldRuntime"/> already uses in this codebase: this
/// class never touches <c>AudioEngine</c> directly, it raises events the App layer applies, and
/// <see cref="Pulse"/> takes the engine's current position/playing state as parameters (same
/// shape as <c>AlbumWorldRuntime.Tick</c>) instead of reading them itself.
/// </summary>
public sealed class SharedPlayJoinRuntime : IDisposable
{
    private const double HardSeekSeconds = 0.8;
    private const double PausedSeekSeconds = 0.35;
    private const int PollIntervalMs = 1000;
    private const int SyncIntervalMs = 250;

    private readonly SharedPlayCdnClient _cdnClient;
    private readonly SharedPlayJoinedPackageStore _packageStore;

    private CancellationTokenSource? _cts;
    private SharedPlayJoinedSession? _session;
    private SharedPlayPlaybackSnapshot? _playback;
    private bool _isJoining;
    private bool _isPolling;
    private long _nextPollTick;
    private long _nextSyncTick;
    private string? _status;

    public SharedPlayJoinRuntime() : this(new SharedPlayCdnClient(), new SharedPlayJoinedPackageStore())
    {
    }

    internal SharedPlayJoinRuntime(SharedPlayCdnClient cdnClient, SharedPlayJoinedPackageStore packageStore)
    {
        _cdnClient = cdnClient;
        _packageStore = packageStore;
    }

    public bool IsJoining => _isJoining;
    public bool IsJoined => _session is not null;
    public bool HasJoinActivity => IsJoining || IsJoined;
    public string? RoomCode => _session?.RoomCode;

    public string? StatusText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_status)) return _status;
            if (_session is null) return null;
            if (_playback is null) return "Shared Play waiting";
            return _playback.IsPlaying ? "Shared Play live" : "Shared Play paused";
        }
    }

    /// <summary>Fired once new audio is downloaded/cached and ready for the App layer to load.</summary>
    public event EventHandler<TrackInfo>? TrackReady;
    public event EventHandler<double>? SeekRequested;
    public event EventHandler? PlayRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? StatusChanged;

    public async Task JoinAsync(SharedPlayJoinRequest request, string? fallbackCdnBaseUrl, CancellationToken outerToken)
    {
        if (string.IsNullOrWhiteSpace(request.RoomCode))
        {
            _status = "The Shared Play join link did not include a room code.";
            StatusChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        Leave();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        _cts = cts;
        _isJoining = true;
        _status = "Joining Shared Play...";
        StatusChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var cdnBaseUrl = SharedPlayDefaults.NormalizeCdnBaseUrl(request.CdnBaseUrl ?? fallbackCdnBaseUrl);
            var cdnBaseUri = new Uri($"{cdnBaseUrl}/");

            _status = "Loading Shared Play session...";
            StatusChanged?.Invoke(this, EventArgs.Empty);

            var session = await FetchSessionAsync(cdnBaseUri, request.RoomCode, cts.Token);

            _status = "Downloading Shared Play package...";
            StatusChanged?.Invoke(this, EventArgs.Empty);

            var audioPath = await _packageStore.GetOrDownloadAudioAsync(session, _cdnClient, cts.Token);
            cts.Token.ThrowIfCancellationRequested();

            _session = session;
            _playback = null;
            _nextPollTick = 0;
            _nextSyncTick = 0;
            _status = "Shared Play session loaded";

            RaiseTrackReady(audioPath);
            await RefreshPlaybackAsync(forceSync: true, enginePositionSeconds: 0, engineIsPlaying: false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Leave(clearStatus: false);
            _status = $"Spectralis could not join this Shared Play session. {ex.Message}";
        }
        finally
        {
            _isJoining = false;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Call on every playback tick while joined; internally throttles CDN polling
    /// (1s) and drift-corrected sync (250ms), same cadence as the legacy client.</summary>
    public void Pulse(double enginePositionSeconds, bool engineIsPlaying)
    {
        if (_session is null || _cts is null) return;

        var now = Environment.TickCount64;
        if (now >= _nextPollTick && !_isPolling)
        {
            _nextPollTick = now + PollIntervalMs;
            _ = RefreshPlaybackAsync(forceSync: false, enginePositionSeconds, engineIsPlaying);
        }

        if (now >= _nextSyncTick)
        {
            _nextSyncTick = now + SyncIntervalMs;
            ApplySync(force: false, enginePositionSeconds, engineIsPlaying);
        }
    }

    public void Leave() => Leave(clearStatus: true);

    private void Leave(bool clearStatus)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _session = null;
        _playback = null;
        _isJoining = false;
        _isPolling = false;
        _nextPollTick = 0;
        _nextSyncTick = 0;

        if (clearStatus)
        {
            _status = null;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RefreshPlaybackAsync(bool forceSync, double enginePositionSeconds, bool engineIsPlaying)
    {
        var session = _session;
        var cts = _cts;
        if (session is null || cts is null || _isPolling) return;

        _isPolling = true;
        try
        {
            var playback = await _cdnClient.FetchPlaybackStateAsync(session.StateUrl, cts.Token);
            if (playback is null)
            {
                _status = "Waiting for host sync state";
                return;
            }

            if (await TryRefreshTrackAsync(session, playback, cts.Token))
                forceSync = true;

            _playback = playback;
            _status = null;
            ApplySync(forceSync, enginePositionSeconds, engineIsPlaying);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            _status = "Shared Play reconnecting";
        }
        finally
        {
            _isPolling = false;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<bool> TryRefreshTrackAsync(
        SharedPlayJoinedSession session,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playback.TrackId) ||
            string.Equals(playback.TrackId, session.TrackId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _status = "Loading next Shared Play track...";
        StatusChanged?.Invoke(this, EventArgs.Empty);

        var baseUri = new Uri($"{session.StateUrl.GetLeftPart(UriPartial.Authority)}/");
        var refreshed = await FetchSessionAsync(baseUri, session.RoomCode, cancellationToken);

        var audioPath = await _packageStore.GetOrDownloadAudioAsync(refreshed, _cdnClient, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        _session = refreshed;
        RaiseTrackReady(audioPath);
        return true;
    }

    private void ApplySync(bool force, double enginePositionSeconds, bool engineIsPlaying)
    {
        if (_playback is not { } playback) return;

        var target = GetHostPosition(playback);
        if (playback.DurationSeconds > 0)
            target = Math.Min(target, playback.DurationSeconds);

        var drift = target - enginePositionSeconds;
        var shouldSeek = force ||
            Math.Abs(drift) >= HardSeekSeconds ||
            (!playback.IsPlaying && Math.Abs(drift) > PausedSeekSeconds);

        if (shouldSeek)
            SeekRequested?.Invoke(this, Math.Max(0, target));

        if (playback.IsPlaying)
        {
            if (!engineIsPlaying) PlayRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            if (engineIsPlaying) PauseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<SharedPlayJoinedSession> FetchSessionAsync(Uri cdnBaseUri, string roomCode, CancellationToken ct)
    {
        var fetched = await _cdnClient.FetchSessionAsync(cdnBaseUri, roomCode, ct);
        return new SharedPlayJoinedSession(
            fetched.RoomCode, fetched.TrackId, fetched.StateUrl, fetched.QueueUrl, fetched.PackageUrl, fetched.ExpiresAtUtc);
    }

    private void RaiseTrackReady(string audioPath)
    {
        TrackInfo trackInfo;
        try { trackInfo = TrackMetadataReader.Read(audioPath); }
        catch { trackInfo = new TrackInfo { SourcePath = audioPath }; }
        TrackReady?.Invoke(this, trackInfo);
    }

    private static double GetHostPosition(SharedPlayPlaybackSnapshot playback)
    {
        var position = playback.PositionSeconds;
        if (playback.IsPlaying)
        {
            var elapsed = DateTimeOffset.UtcNow - playback.HostClockUtc;
            if (elapsed > TimeSpan.Zero)
                position += elapsed.TotalSeconds;
        }
        return Math.Max(0, position);
    }

    public void Dispose() => Leave();
}
