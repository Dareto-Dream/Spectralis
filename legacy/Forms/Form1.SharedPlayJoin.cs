using System.Diagnostics;

namespace Spectralis;

public partial class Form1
{
    private const double JoinedSharedPlayHardSeekSeconds = 0.8;
    private const double JoinedSharedPlayPausedSeekSeconds = 0.35;
    private const int JoinedSharedPlayPollIntervalMilliseconds = 1000;
    private const int JoinedSharedPlaySyncIntervalMilliseconds = 250;

    private bool HasJoinedSharedPlayActivity => isJoiningSharedPlay || joinedSharedPlaySession is not null;

    private async Task JoinSharedPlaySessionAsync(SharedPlayJoinRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            ShowError("The Shared Play join link did not include a session ID.", "Shared Play");
            return;
        }

        StopJoinedSharedPlaySession(stopPlayback: false, clearStatus: false);
        sharedPlay.ClearActiveSession();

        var cancellation = new CancellationTokenSource();
        joinedSharedPlayCancellation = cancellation;
        isJoiningSharedPlay = true;
        joinedSharedPlayStatus = "Joining Shared Play...";
        UpdateUiState();

        try
        {
            var cdnBaseUrl = SharedPlayDefaults.NormalizeCdnBaseUrl(request.CdnBaseUrl ?? appSettings.SharedPlayCdnBaseUrl);
            var cdnBaseUri = new Uri($"{cdnBaseUrl}/");

            joinedSharedPlayStatus = "Loading Shared Play session...";
            UpdateUiState();

            var remoteSession = await sharedPlayReceiverClient.FetchSessionAsync(
                cdnBaseUri,
                request.SessionId,
                cancellation.Token);

            joinedSharedPlayStatus = "Downloading Shared Play package...";
            UpdateUiState();

            var audioPath = await sharedPlayJoinedPackageStore.GetOrDownloadAudioAsync(
                remoteSession,
                sharedPlayReceiverClient,
                cancellation.Token);

            cancellation.Token.ThrowIfCancellationRequested();

            StopLocalPlaybackForExternalUrl();
            engine.Load(audioPath);
            visualizerControl.ClearFrame();
            trackBarSeek.Value = 0;

            joinedSharedPlaySession = remoteSession;
            joinedSharedPlayPlayback = null;
            nextJoinedSharedPlayPollTick = 0;
            nextJoinedSharedPlaySyncTick = 0;
            joinedSharedPlayStatus = "Shared Play session loaded";

            await RefreshJoinedSharedPlayPlaybackAsync(forceSync: true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Shared Play join failed: {ex}");
            StopJoinedSharedPlaySession(stopPlayback: false, clearStatus: true);
            ShowError(
                $"Spectralis could not join this Shared Play session.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Shared Play");
        }
        finally
        {
            if (ReferenceEquals(joinedSharedPlayCancellation, cancellation))
            {
                isJoiningSharedPlay = false;
                if (joinedSharedPlaySession is null)
                {
                    joinedSharedPlayCancellation = null;
                    cancellation.Dispose();
                }
            }

            UpdateUiState();
        }
    }

    private void PulseJoinedSharedPlay()
    {
        if (joinedSharedPlaySession is null || joinedSharedPlayCancellation is null)
            return;

        var now = Environment.TickCount64;
        if (now >= nextJoinedSharedPlayPollTick && !isPollingJoinedSharedPlay)
        {
            nextJoinedSharedPlayPollTick = now + JoinedSharedPlayPollIntervalMilliseconds;
            _ = RefreshJoinedSharedPlayPlaybackAsync(forceSync: false);
        }

        if (now >= nextJoinedSharedPlaySyncTick)
        {
            nextJoinedSharedPlaySyncTick = now + JoinedSharedPlaySyncIntervalMilliseconds;
            ApplyJoinedSharedPlaySync(force: false);
        }
    }

    private async Task RefreshJoinedSharedPlayPlaybackAsync(bool forceSync)
    {
        var session = joinedSharedPlaySession;
        var cancellation = joinedSharedPlayCancellation;
        if (session is null || cancellation is null || isPollingJoinedSharedPlay)
            return;

        isPollingJoinedSharedPlay = true;
        try
        {
            var playback = await sharedPlayReceiverClient.FetchPlaybackStateAsync(
                session.StateUrl,
                cancellation.Token);

            if (playback is null)
            {
                joinedSharedPlayStatus = "Waiting for host sync state";
                return;
            }

            if (await TryRefreshJoinedSharedPlayTrackAsync(session, playback, cancellation.Token))
                forceSync = true;

            joinedSharedPlayPlayback = playback;
            joinedSharedPlayStatus = null;
            ApplyJoinedSharedPlaySync(forceSync);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Shared Play sync failed: {ex}");
            joinedSharedPlayStatus = "Shared Play reconnecting";
        }
        finally
        {
            isPollingJoinedSharedPlay = false;
            UpdateUiState();
        }
    }

    private async Task<bool> TryRefreshJoinedSharedPlayTrackAsync(
        SharedPlayRemoteSession session,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(playback.TrackId) ||
            string.Equals(playback.TrackId, session.TrackId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        joinedSharedPlayStatus = "Loading next Shared Play track...";
        UpdateUiState();

        var baseUri = new Uri($"{session.StateUrl.GetLeftPart(UriPartial.Authority)}/");
        var refreshedSession = await sharedPlayReceiverClient.FetchSessionAsync(
            baseUri,
            session.SessionId,
            cancellationToken);

        var audioPath = await sharedPlayJoinedPackageStore.GetOrDownloadAudioAsync(
            refreshedSession,
            sharedPlayReceiverClient,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        StopLocalPlaybackForExternalUrl();
        engine.Load(audioPath);
        engine.Volume = trackBarVolume.Value / 100f;
        visualizerControl.ClearFrame();
        trackBarSeek.Value = 0;
        joinedSharedPlaySession = refreshedSession;
        return true;
    }

    private void ApplyJoinedSharedPlaySync(bool force)
    {
        if (joinedSharedPlayPlayback is not { } playback || !engine.IsLoaded)
            return;

        isApplyingJoinedSharedPlaySync = true;
        try
        {
            var target = GetJoinedSharedPlayHostPosition(playback);
            var duration = playback.DurationSeconds > 0
                ? playback.DurationSeconds
                : engine.GetLength();

            if (duration > 0)
                target = Math.Min(target, duration);

            var position = engine.GetPosition();
            var drift = target - position;
            var shouldSeek = force ||
                Math.Abs(drift) >= JoinedSharedPlayHardSeekSeconds ||
                (!playback.IsPlaying && Math.Abs(drift) > JoinedSharedPlayPausedSeekSeconds);

            if (shouldSeek)
                engine.Seek((float)Math.Max(0, target));

            if (playback.IsPlaying)
            {
                if (!engine.IsPlaying)
                    engine.Play();
            }
            else
            {
                if (engine.IsPlaying)
                    engine.Pause();
            }
        }
        finally
        {
            isApplyingJoinedSharedPlaySync = false;
        }
    }

    private void StopJoinedSharedPlaySession(bool stopPlayback, bool clearStatus)
    {
        joinedSharedPlayCancellation?.Cancel();
        joinedSharedPlayCancellation?.Dispose();
        joinedSharedPlayCancellation = null;
        joinedSharedPlaySession = null;
        joinedSharedPlayPlayback = null;
        isJoiningSharedPlay = false;
        isPollingJoinedSharedPlay = false;
        isApplyingJoinedSharedPlaySync = false;
        nextJoinedSharedPlayPollTick = 0;
        nextJoinedSharedPlaySyncTick = 0;

        if (clearStatus)
            joinedSharedPlayStatus = null;

        if (stopPlayback && engine.IsLoaded)
            engine.Stop();
    }

    private string? GetJoinedSharedPlayStatusText()
    {
        if (!string.IsNullOrWhiteSpace(joinedSharedPlayStatus))
            return joinedSharedPlayStatus;

        if (joinedSharedPlaySession is null)
            return null;

        if (joinedSharedPlayPlayback is null)
            return "Shared Play waiting";

        return joinedSharedPlayPlayback.IsPlaying ? "Shared Play live" : "Shared Play paused";
    }

    private static double GetJoinedSharedPlayHostPosition(SharedPlayPlaybackSnapshot playback)
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
}
