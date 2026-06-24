using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Spectralis;

public partial class Form1
{
    private const string SpotifyWebPlayerHost = "spectralis.spotify.local";
    private static readonly TimeSpan SpotifyDeviceReadyTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan SpotifyDeviceReadyPollInterval = TimeSpan.FromMilliseconds(200);

    private readonly SpotifyService spotifyService = new();
    private readonly object spotifyInitializationGate = new();
    private WebView2? spotifyWebView;
    private VisualizerSampleProvider? spotifyVisualizer;
    private SpotifyLoopbackCapture? spotifyLoopback;
    private AudioTrackInfo? spotifyCurrentTrack;
    private bool spotifyIsPlaying;
    private float spotifyPositionSeconds;
    private long spotifyPositionSetAt;
    private float spotifyDurationSeconds;
    private string? spotifyDeviceId;
    private string? spotifyLastTrackId;
    private string? spotifyStatusMessage;
    private CancellationTokenSource? spotifyArtCts;
    private bool spotifyWebPlaybackUnsupported;
    private IReadOnlyList<QueueListItem> spotifyQueueItems = [];
    private DateTime spotifyLastQueueRefreshUtc = DateTime.MinValue;
    private bool spotifyQueueRefreshInProgress;
    private bool suppressSpotifyStateForLocalPlayback;
    private bool resumeSpotifyAfterLocalPlayback;
    private bool advanceSpotifyAfterLocalPlayback;
    private bool spotifyLocalInterludeActive;
    private bool spotifyLocalInterludePreviousShuffle;
    private RepeatMode spotifyLocalInterludePreviousRepeat;
    private Task? spotifyInitializationTask;
    private long spotifyPlaybackToken;
    private bool spotifyAcceptState;

    internal static readonly string SpotifyLogPath =
        AppLogPaths.For("spotify.log");

    internal bool IsSpotifyActive => spotifyCurrentTrack is not null;

    private float SpotifyCurrentPositionSeconds
    {
        get
        {
            if (!spotifyIsPlaying || spotifyPositionSetAt == 0)
                return spotifyPositionSeconds;
            var elapsed = (float)((Environment.TickCount64 - spotifyPositionSetAt) / 1000.0);
            return Math.Min(spotifyPositionSeconds + elapsed, spotifyDurationSeconds);
        }
    }
    internal bool IsSpotifyReady => spotifyDeviceId is not null;
    private string SpotifyClientId => SpotifyClientIdProvider.ResolveClientId(appSettings.SpotifyClientId);

    internal VisualizerFrame GetSpotifyVisualizerFrame() =>
        spotifyVisualizer?.GetFrame() ?? VisualizerFrame.Empty;

    private long BeginSpotifyHandoff()
    {
        CancelSpotifyLocalHandoff();
        spotifyPlaybackToken++;
        spotifyAcceptState = true;
        suppressSpotifyStateForLocalPlayback = false;
        return spotifyPlaybackToken;
    }

    private void InvalidateSpotifyHandoff()
    {
        spotifyPlaybackToken++;
        spotifyAcceptState = false;
    }

    private void EndSpotifyHandoff(long handoffToken)
    {
        if (spotifyPlaybackToken != handoffToken)
            return;

        spotifyPlaybackToken++;
        spotifyAcceptState = false;
    }

    private bool CanAcceptSpotifyState() =>
        spotifyAcceptState && !suppressSpotifyStateForLocalPlayback;

    private bool IsSpotifyHandoffCurrent(long handoffToken) =>
        spotifyPlaybackToken == handoffToken && CanAcceptSpotifyState();

    internal Task InitializeSpotifyAsync()
    {
        if (!spotifyService.IsLinked)
            return Task.CompletedTask;

        lock (spotifyInitializationGate)
        {
            if (spotifyWebView is not null)
                return spotifyInitializationTask ?? Task.CompletedTask;

            spotifyInitializationTask ??= InitializeSpotifyCoreAsync();
            return spotifyInitializationTask;
        }
    }

    private async Task InitializeSpotifyCoreAsync()
    {
        try
        {
            await InitializeSpotifyWebViewAsync();
        }
        finally
        {
            lock (spotifyInitializationGate)
            {
                spotifyInitializationTask = null;
            }
        }
    }

    private async Task InitializeSpotifyWebViewAsync()
    {
        var token = await spotifyService.GetFreshAccessTokenAsync(SpotifyClientId);
        if (token is null)
        {
            SetSpotifyStatus("Spotify needs to be linked again");
            return;
        }

        try
        {
            LogSpotify("InitializeSpotifyAsync start");
            SetSpotifyStatus("Spotify starting...");
            spotifyWebPlaybackUnsupported = false;
            spotifyDeviceId = null;
            spotifyWebView = new WebView2
            {
                Enabled = false,
                Location = new System.Drawing.Point(-20, -20),
                Size = new System.Drawing.Size(2, 2),
                TabStop = false,
                Visible = true
            };
            Controls.Add(spotifyWebView);
            spotifyWebView.SendToBack();

            var environment = await CreateSpotifyWebViewEnvironmentAsync();
            await spotifyWebView.EnsureCoreWebView2Async(environment);
            spotifyWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            spotifyWebView.CoreWebView2.WebMessageReceived += OnSpotifyWebMessage;
            spotifyWebView.CoreWebView2.NavigationCompleted += OnSpotifyNavigationCompleted;
            spotifyWebView.CoreWebView2.ProcessFailed += OnSpotifyProcessFailed;
            spotifyWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                SpotifyWebPlayerHost,
                GetSpotifyWebPlayerDirectory(),
                CoreWebView2HostResourceAccessKind.Allow);

            await LoadSpotifyPlayerAsync(token);
        }
        catch (Exception ex)
        {
            LogSpotify($"InitializeSpotifyAsync failed: {ex}");
            spotifyWebView?.Dispose();
            spotifyWebView = null;
            spotifyWebPlaybackUnsupported = true;
            SetSpotifyStatus("Spotify in-app playback unavailable");
        }
    }

    private async Task<CoreWebView2Environment> CreateSpotifyWebViewEnvironmentAsync()
    {
        var userDataFolder = Path.Combine(Path.GetTempPath(), "spectralis-spotify-webview2");
        Directory.CreateDirectory(userDataFolder);

        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--disable-features=AudioServiceOutOfProcess --autoplay-policy=no-user-gesture-required"
        };

        LogSpotify($"Creating Spotify WebView2 environment userDataFolder={userDataFolder} args={options.AdditionalBrowserArguments}");
        return await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
    }

    internal async Task ReloadSpotifyPlayerAsync()
    {
        if (spotifyWebView is null)
        {
            await InitializeSpotifyAsync();
            return;
        }

        var token = await spotifyService.GetFreshAccessTokenAsync(SpotifyClientId);
        if (token is not null)
            await LoadSpotifyPlayerAsync(token);
    }

    private async Task LoadSpotifyPlayerAsync(string accessToken)
    {
        if (spotifyWebView?.CoreWebView2 is null) return;

        var playerPath = Path.Combine(GetSpotifyWebPlayerDirectory(), "player.html");
        await File.WriteAllTextAsync(playerPath, BuildSpotifyPlayerHtml(accessToken));

        var url = $"https://{SpotifyWebPlayerHost}/player.html?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        LogSpotify($"Navigating Spotify Web Playback SDK host: {url}");
        spotifyWebView.CoreWebView2.Navigate(url);
    }

    private static string GetSpotifyWebPlayerDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "spectralis-spotify-web-player");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string BuildSpotifyPlayerHtml(string accessToken)
    {
        var tokenJson = JsonSerializer.Serialize(accessToken);
        return $$"""
            <!DOCTYPE html>
            <html><head>
            <meta charset="utf-8">
            </head><body>
            <script src="https://sdk.scdn.co/spotify-player.js"></script>
            <script>
            window.spotifyToken = {{tokenJson}};
            const post = payload => window.chrome.webview.postMessage(JSON.stringify(payload));
            const postError = (kind, message) => post({type:'error', kind, message: message || kind});
            const log = message => post({type:'log', message});
            window.addEventListener('error', event => postError('window_error', event.message || 'Window error'));
            window.addEventListener('unhandledrejection', event => {
                const reason = event.reason && (event.reason.message || event.reason);
                postError('unhandled_rejection', reason || 'Unhandled promise rejection');
            });
            log('Spotify host loaded. secureContext=' + window.isSecureContext + ' origin=' + location.origin);
            const postState = state => {
                if (!state || !state.track_window || !state.track_window.current_track) {
                    post({type:'no_state'});
                    return;
                }
                const t = state.track_window.current_track;
                post({
                    type: 'state_changed',
                    isPaused: state.paused,
                    position: state.position,
                    duration: state.duration,
                    track: {
                        id: t.id,
                        name: t.name,
                        artist: t.artists.map(a => a.name).join(', '),
                        album: t.album.name,
                        albumArtUrl: t.album.images.length > 0 ? t.album.images[0].url : null,
                        duration: state.duration
                    }
                });
            };
            window.onSpotifyWebPlaybackSDKReady = () => {
                log('Spotify Web Playback SDK ready callback fired');
                const player = new Spotify.Player({
                    name: 'Spectralis',
                    getOAuthToken: cb => {
                        let completed = false;
                        window.pendingTokenCb = t => {
                            if (completed) return;
                            completed = true;
                            window.spotifyToken = t;
                            cb(t);
                        };
                        post({type:'token_request'});
                        window.setTimeout(() => {
                            if (!completed && window.spotifyToken) {
                                completed = true;
                                cb(window.spotifyToken);
                            }
                        }, 750);
                    },
                    volume: 1.0
                });
                player.addListener('ready', ({device_id}) => {
                    log('Spotify player ready with device_id=' + device_id);
                    post({type:'ready',deviceId:device_id});
                });
                player.addListener('not_ready', ({device_id}) => {
                    log('Spotify player not_ready with device_id=' + device_id);
                    post({type:'not_ready',deviceId:device_id});
                });
                player.addListener('initialization_error', ({message}) => postError('initialization_error', message));
                player.addListener('authentication_error', ({message}) => postError('authentication_error', message));
                player.addListener('account_error', ({message}) => postError('account_error', message));
                player.addListener('playback_error', ({message}) => postError('playback_error', message));
                player.addListener('autoplay_failed', () => {
                    postError('autoplay_failed', 'Spotify playback was blocked until Spectralis receives a Play click.');
                });
                player.addListener('player_state_changed', state => {
                    postState(state);
                });
                window.spotifyPlayer = player;
                player.connect().then(success => {
                    log('Spotify player.connect() resolved: ' + success);
                    post({type:'connect_result', success});
                }).catch(e => postError('connect_error', e && e.message));
            };
            window.spotifyActivate = async () => {
                try {
                    if (window.spotifyPlayer && window.spotifyPlayer.activateElement) {
                        await window.spotifyPlayer.activateElement();
                    }
                    return true;
                } catch (e) {
                    postError('activate_error', e && e.message);
                    return false;
                }
            };
            window.spotifyResume = async () => {
                try {
                    if (window.spotifyPlayer && window.spotifyPlayer.resume) {
                        await window.spotifyPlayer.resume();
                    }
                    return true;
                } catch (e) {
                    postError('resume_error', e && e.message);
                    return false;
                }
            };
            window.spotifyPullState = async () => {
                try {
                    if (!window.spotifyPlayer) {
                        post({type:'no_state'});
                        return;
                    }
                    postState(await window.spotifyPlayer.getCurrentState());
                } catch (e) {
                    postError('state_error', e && e.message);
                }
            };
            window.provideToken = t => {
                window.spotifyToken = t;
                const pending = window.pendingTokenCb;
                window.pendingTokenCb = null;
                if (pending) pending(t);
            };
            </script>
            </body></html>
            """;
    }

    private async void OnSpotifyWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();
            LogSpotify($"message: {type}");

            switch (type)
            {
                case "log":
                    var logMessage = root.TryGetProperty("message", out var logEl) ? logEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(logMessage))
                        LogSpotify($"[JS] {logMessage}");
                    break;

                case "ready":
                    spotifyDeviceId = root.GetProperty("deviceId").GetString();
                    spotifyWebPlaybackUnsupported = false;
                    LogSpotify($"ready deviceId={spotifyDeviceId}");
                    SetSpotifyStatus("Spotify ready");
                    break;

                case "not_ready":
                    var offlineDeviceId = root.TryGetProperty("deviceId", out var notReadyDeviceId)
                        ? notReadyDeviceId.GetString()
                        : null;
                    if (offlineDeviceId == spotifyDeviceId)
                    {
                        spotifyDeviceId = null;
                    }
                    LogSpotify($"not_ready deviceId={offlineDeviceId}");
                    SetSpotifyStatus("Spotify device went offline");
                    break;

                case "connect_result":
                    var connectSuccess = root.TryGetProperty("success", out var successEl) && successEl.GetBoolean();
                    LogSpotify($"connect_result success={connectSuccess}");
                    if (!connectSuccess)
                        SetSpotifyStatus("Spotify player could not connect");
                    break;

                case "token_request":
                    LogSpotify("token_request");
                    var fresh = await spotifyService.GetFreshAccessTokenAsync(SpotifyClientId);
                    if (fresh is not null && spotifyWebView?.CoreWebView2 is not null)
                    {
                        await spotifyWebView.CoreWebView2.ExecuteScriptAsync(
                            $"window.provideToken({JsonSerializer.Serialize(fresh)})");
                        LogSpotify("token_request fulfilled");
                    }
                    else
                    {
                        LogSpotify("token_request failed: no fresh token");
                    }
                    break;

                case "state_changed":
                    if (!CanAcceptSpotifyState())
                    {
                        LogSpotify("state_changed ignored: Spotify is not the active handoff");
                        break;
                    }

                    spotifyStatusMessage = null;
                    await HandleSpotifyStateChangedAsync(root, spotifyPlaybackToken);
                    break;

                case "no_state":
                    LogSpotify("no_state");
                    if (!IsSpotifyActive && !suppressSpotifyStateForLocalPlayback)
                        SetSpotifyStatus(IsSpotifyReady ? "Spotify ready" : "Spotify starting...");
                    break;

                case "error":
                    LogSpotify($"error kind={GetString(root, "kind")} message={GetString(root, "message")}");
                    if (root.TryGetProperty("kind", out var kindEl) &&
                        string.Equals(kindEl.GetString(), "initialization_error", StringComparison.OrdinalIgnoreCase))
                    {
                        spotifyWebPlaybackUnsupported = true;
                        spotifyDeviceId = null;
                    }
                    SetSpotifyStatus(GetSpotifyErrorStatus(root));
                    break;
            }
        }
        catch (Exception ex)
        {
            LogSpotify($"OnSpotifyWebMessage exception: {ex}");
        }
    }

    private void OnSpotifyNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        LogSpotify($"NavigationCompleted success={e.IsSuccess} httpStatus={e.HttpStatusCode} webError={e.WebErrorStatus}");
    }

    private void OnSpotifyProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        LogSpotify($"ProcessFailed kind={e.ProcessFailedKind} reason={e.Reason} exitCode={e.ExitCode}");
    }

    private async Task HandleSpotifyStateChangedAsync(JsonElement root, long handoffToken)
    {
        if (!IsSpotifyHandoffCurrent(handoffToken))
            return;

        var isPaused = root.GetProperty("isPaused").GetBoolean();
        var posMs = root.GetProperty("position").GetDouble();
        var durMs = root.GetProperty("duration").GetDouble();

        if (!root.TryGetProperty("track", out var tEl)) return;

        var trackId = tEl.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var name = tEl.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
        var artist = tEl.TryGetProperty("artist", out var artistEl) ? artistEl.GetString() : null;
        var album = tEl.TryGetProperty("album", out var albumEl) ? albumEl.GetString() : null;
        var artUrl = tEl.TryGetProperty("albumArtUrl", out var artEl) ? artEl.GetString() : null;

        await ApplySpotifyPlaybackStateAsync(handoffToken, isPaused, posMs, durMs, trackId, name, artist, album, artUrl);
    }

    private async Task ApplySpotifyPlaybackStateAsync(
        long handoffToken,
        bool isPaused,
        double posMs,
        double durMs,
        string? trackId,
        string name,
        string? artist,
        string? album,
        string? artUrl)
    {
        if (!IsSpotifyHandoffCurrent(handoffToken))
            return;

        spotifyIsPlaying = !isPaused;
        spotifyPositionSeconds = (float)(posMs / 1000.0);
        spotifyPositionSetAt = Environment.TickCount64;
        spotifyDurationSeconds = (float)(durMs / 1000.0);

        var isNewTrack = trackId != spotifyLastTrackId;
        if (isNewTrack)
        {
            spotifyLastTrackId = trackId;
            spotifyArtCts?.Cancel();
            var cts = spotifyArtCts = new CancellationTokenSource();

            var artTask = artUrl is not null
                ? FetchBytesAsync(artUrl, cts.Token)
                : Task.FromResult<byte[]?>(null);

            var lyricsTask = trackId is not null
                ? SpotifyLyricsService.FetchAsync(trackId, cts.Token)
                : Task.FromResult<LyricsDocument?>(null);

            await Task.WhenAll(artTask, lyricsTask);

            if (!cts.IsCancellationRequested && IsSpotifyHandoffCurrent(handoffToken))
            {
                var artBytes = artTask.IsCompletedSuccessfully ? artTask.Result : null;
                var lyrics = lyricsTask.IsCompletedSuccessfully ? lyricsTask.Result : null;

                spotifyCurrentTrack = new AudioTrackInfo(
                    $"spotify:{trackId ?? "unknown"}",
                    name, artist, album, artBytes,
                    lyrics, null, null, null, null, null,
                    "Spotify", 2, 44100, 16,
                    TimeSpan.FromMilliseconds(durMs));

                spotifyVisualizer ??= new VisualizerSampleProvider(
                    new SignalGenerator(44100, 2) { Gain = 0 });

                UpdateAlbumArt(spotifyCurrentTrack);
            }
        }

        if (!IsSpotifyHandoffCurrent(handoffToken))
            return;

        if (spotifyIsPlaying && spotifyLoopback is null && spotifyVisualizer is not null)
        {
            spotifyLoopback = new SpotifyLoopbackCapture();
            var routed = spotifyLoopback.Start(spotifyVisualizer);

            LogSpotify(routed
                ? $"Spotify audio capture started mode={spotifyLoopback.LastStartMode}"
                : $"Spotify audio capture failed mode={spotifyLoopback.LastStartMode}");
            if (!routed)
                SetSpotifyStatus("Spotify is playing, but visualizer audio capture failed");
        }
        else if (!spotifyIsPlaying && spotifyLoopback is not null)
        {
            spotifyLoopback.Stop();
            spotifyLoopback.Dispose();
            spotifyLoopback = null;
        }

        await RefreshSpotifyQueueAsync(handoffToken: handoffToken);
        if (!IsSpotifyHandoffCurrent(handoffToken))
            return;

        UpdateUiState();
    }

    internal async Task<bool> SpotifyTransferPlaybackAsync()
    {
        if (spotifyDeviceId is null) return false;
        var transferred = await spotifyService.TransferPlaybackAsync(spotifyDeviceId, SpotifyClientId);
        SetSpotifyStatus(transferred ? "Spotify transfer requested" : "Spotify transfer failed");
        return transferred;
    }

    internal async Task LoadSpotifyUriAsync(string playbackUri)
    {
        if (string.IsNullOrWhiteSpace(playbackUri))
            return;

        var handoffToken = BeginSpotifyHandoff();

        if (IsYouTubeActive)
            StopYouTubePlayback();
        if (IsSoundCloudActive)
            StopSoundCloudPlayback();
        if (IsSunoActive)
            StopSunoPlayback();

        StopRemoteAudioPlayback();
        if (engine.IsLoaded)
            engine.Unload();
        if (IsAlbumWorldActive)
            UnloadAlbumCapsule();
        UnloadCapsule();

        if (!spotifyService.IsLinked)
        {
            EndSpotifyHandoff(handoffToken);
            SetSpotifyStatus("Spotify needs to be linked");
            ShowError("Link Spotify in Spectralis settings before opening Spotify links from the browser.", "Spotify");
            return;
        }

        var normalizedPlaybackUri = playbackUri.Trim();
        if (!await EnsureSpotifyPlaybackDeviceReadyAsync(showError: true) ||
            spotifyDeviceId is null ||
            !IsSpotifyHandoffCurrent(handoffToken))
        {
            EndSpotifyHandoff(handoffToken);
            return;
        }

        var started = await TryStartSpotifyUriPlaybackWithRetriesAsync(normalizedPlaybackUri, handoffToken);
        if (!IsSpotifyHandoffCurrent(handoffToken))
        {
            if (started)
                _ = spotifyService.PauseAsync(SpotifyClientId, spotifyDeviceId);
            return;
        }

        if (!started)
        {
            EndSpotifyHandoff(handoffToken);
            SetSpotifyStatus("Spotify link playback failed");
            ShowError("Spotify could not start playback for that link. Premium playback and an available Spectralis Spotify device are required.", "Spotify");
            return;
        }

        SetSpotifyStatus("Spotify playback requested");
        await ResumeSpotifyPlaybackAsync(handoffToken);
        await PollForSpotifyPlayingAsync(handoffToken);
        await RefreshSpotifyQueueAsync(force: true, handoffToken: handoffToken);
        if (!IsSpotifyHandoffCurrent(handoffToken))
            return;

        UpdateUiState();
    }

    internal void StopSpotifyPlayback()
    {
        InvalidateSpotifyHandoff();
        spotifyArtCts?.Cancel();
        spotifyLoopback?.Stop();
        spotifyLoopback?.Dispose();
        spotifyLoopback = null;
        spotifyCurrentTrack = null;
        spotifyIsPlaying = false;
        spotifyPositionSeconds = 0;
        spotifyPositionSetAt = 0;
        spotifyDurationSeconds = 0;
        spotifyLastTrackId = null;
        spotifyQueueItems = [];
        spotifyVisualizer?.Clear();
    }

    private void ParkSpotifyForLocalPlayback(bool resumeAfterLocalPlayback, bool advanceOnResume)
    {
        var hasSpotifyConnection = spotifyService.IsLinked;
        var shouldResumeSpotify =
            resumeAfterLocalPlayback &&
            hasSpotifyConnection &&
            spotifyCurrentTrack is not null &&
            spotifyIsPlaying;

        resumeSpotifyAfterLocalPlayback = shouldResumeSpotify;
        advanceSpotifyAfterLocalPlayback = shouldResumeSpotify && advanceOnResume;
        suppressSpotifyStateForLocalPlayback = hasSpotifyConnection;

        if (hasSpotifyConnection && spotifyIsPlaying)
            _ = spotifyService.PauseAsync(SpotifyClientId, spotifyDeviceId);

        StopSpotifyPlayback();
    }

    private void CancelSpotifyLocalHandoff()
    {
        resumeSpotifyAfterLocalPlayback = false;
        advanceSpotifyAfterLocalPlayback = false;
        suppressSpotifyStateForLocalPlayback = false;
        RestoreSpotifyLocalInterludeQueueMode();
    }

    private void BeginSpotifyLocalInterlude()
    {
        if (!spotifyLocalInterludeActive)
        {
            spotifyLocalInterludePreviousShuffle = queue.Shuffle;
            spotifyLocalInterludePreviousRepeat = queue.Repeat;
            spotifyLocalInterludeActive = true;
        }

        queue.Shuffle = false;
        queue.Repeat = RepeatMode.None;
        UpdateQueueModeButtons();
    }

    private void RestoreSpotifyLocalInterludeQueueMode()
    {
        if (!spotifyLocalInterludeActive)
            return;

        queue.Shuffle = spotifyLocalInterludePreviousShuffle;
        queue.Repeat = spotifyLocalInterludePreviousRepeat;
        spotifyLocalInterludeActive = false;
        UpdateQueueModeButtons();
    }

    private async Task ResumeSpotifyAfterLocalPlaybackAsync()
    {
        if (!resumeSpotifyAfterLocalPlayback)
            return;

        var shouldAdvance = advanceSpotifyAfterLocalPlayback;
        resumeSpotifyAfterLocalPlayback = false;
        advanceSpotifyAfterLocalPlayback = false;
        RestoreSpotifyLocalInterludeQueueMode();
        var handoffToken = BeginSpotifyHandoff();

        if (!await EnsureSpotifyPlaybackDeviceReadyAsync(showError: false) ||
            !IsSpotifyHandoffCurrent(handoffToken))
        {
            EndSpotifyHandoff(handoffToken);
            SetSpotifyStatus("Spotify in-app playback is not ready");
            UpdateUiState();
            return;
        }

        if (shouldAdvance)
        {
            await spotifyService.NextTrackAsync(SpotifyClientId, spotifyDeviceId);
            if (!IsSpotifyHandoffCurrent(handoffToken))
                return;

            await Task.Delay(500);
            if (!IsSpotifyHandoffCurrent(handoffToken))
                return;
        }

        await spotifyService.TransferPlaybackAsync(spotifyDeviceId!, SpotifyClientId);
        if (!IsSpotifyHandoffCurrent(handoffToken))
            return;

        await spotifyService.ResumeAsync(SpotifyClientId, spotifyDeviceId);
        if (!IsSpotifyHandoffCurrent(handoffToken))
            return;

        await ResumeSpotifyPlaybackAsync(handoffToken);
        await PollForSpotifyPlayingAsync(handoffToken);
        await RefreshSpotifyQueueAsync(force: true, handoffToken: handoffToken);
        if (!IsSpotifyHandoffCurrent(handoffToken))
            return;

        UpdateUiState();
    }

    internal async Task SpotifyPlayPauseAsync()
    {
        var handoffToken = BeginSpotifyHandoff();

        if (!await EnsureSpotifyPlaybackDeviceReadyAsync(showError: true) ||
            !IsSpotifyHandoffCurrent(handoffToken))
        {
            EndSpotifyHandoff(handoffToken);
            return;
        }

        await ActivateSpotifyPlaybackAsync(handoffToken);

        if (!IsSpotifyActive && spotifyDeviceId is not null)
        {
            await SpotifyTransferPlaybackAsync();
            if (!IsSpotifyHandoffCurrent(handoffToken))
                return;

            await Task.Delay(500);
            if (!IsSpotifyHandoffCurrent(handoffToken))
                return;

            await ResumeSpotifyPlaybackAsync(handoffToken);
            await PullSpotifyStateAsync(handoffToken);
            return;
        }

        if (spotifyIsPlaying)
        {
            await spotifyService.PauseAsync(SpotifyClientId, spotifyDeviceId);
            await PullSpotifyStateAsync(handoffToken);
        }
        else
        {
            await spotifyService.ResumeAsync(SpotifyClientId, spotifyDeviceId);
            await ResumeSpotifyPlaybackAsync(handoffToken);
            await PullSpotifyStateAsync(handoffToken);
        }
    }

    internal async Task SpotifySeekAsync(float seconds)
    {
        if (!await EnsureSpotifyPlaybackDeviceReadyAsync(showError: false))
            return;

        var handoffToken = spotifyPlaybackToken;
        await spotifyService.SeekAsync((int)(seconds * 1000), SpotifyClientId, spotifyDeviceId);
        if (!IsSpotifyHandoffCurrent(handoffToken))
            return;

        spotifyPositionSeconds = seconds;
        spotifyPositionSetAt = Environment.TickCount64;
        await PullSpotifyStateAsync(handoffToken);
    }

    internal async Task SpotifyNextAsync()
    {
        if (!await EnsureSpotifyPlaybackDeviceReadyAsync(showError: false))
            return;

        var handoffToken = spotifyPlaybackToken;
        await spotifyService.NextTrackAsync(SpotifyClientId, spotifyDeviceId);
        if (!IsSpotifyHandoffCurrent(handoffToken))
            return;

        await Task.Delay(400);
        if (!IsSpotifyHandoffCurrent(handoffToken))
            return;

        await PullSpotifyStateAsync(handoffToken);
        await RefreshSpotifyQueueAsync(force: true, handoffToken: handoffToken);
    }

    internal async Task SpotifyPreviousAsync()
    {
        if (!await EnsureSpotifyPlaybackDeviceReadyAsync(showError: false))
            return;

        var handoffToken = spotifyPlaybackToken;
        await spotifyService.PreviousTrackAsync(SpotifyClientId, spotifyDeviceId);
        if (!IsSpotifyHandoffCurrent(handoffToken))
            return;

        await Task.Delay(400);
        if (!IsSpotifyHandoffCurrent(handoffToken))
            return;

        await PullSpotifyStateAsync(handoffToken);
        await RefreshSpotifyQueueAsync(force: true, handoffToken: handoffToken);
    }

    private async Task<bool> EnsureSpotifyPlaybackDeviceReadyAsync(bool showError)
    {
        if (spotifyDeviceId is not null)
            return true;

        if (spotifyWebView is null || spotifyInitializationTask is not null)
            await InitializeSpotifyAsync();

        if (spotifyDeviceId is not null)
            return true;

        if (!spotifyWebPlaybackUnsupported)
        {
            SetSpotifyStatus("Waiting for Spotify player...");
            if (await WaitForSpotifyDeviceReadyAsync(SpotifyDeviceReadyTimeout))
                return true;
        }

        var message = spotifyWebPlaybackUnsupported
            ? "Spotify in-app playback is unavailable. Spectralis will not route Spotify through the desktop app because OBS needs the audio inside Spectralis."
            : spotifyStatusMessage ?? "Spotify is not ready yet. Try again in a few seconds.";

        LogSpotify($"device not ready: {message}");
        SetSpotifyStatus(message);

        if (showError)
            ShowError($"{message}{Environment.NewLine}{Environment.NewLine}Log: {SpotifyLogPath}", "Spotify");

        return false;
    }

    private async Task<bool> WaitForSpotifyDeviceReadyAsync(TimeSpan timeout)
    {
        var deadlineUtc = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadlineUtc)
        {
            if (spotifyDeviceId is not null)
                return true;

            if (spotifyWebPlaybackUnsupported)
                return false;

            await Task.Delay(SpotifyDeviceReadyPollInterval);
        }

        return spotifyDeviceId is not null;
    }

    private async Task<bool> TryStartSpotifyUriPlaybackWithRetriesAsync(string playbackUri, long handoffToken)
    {
        const int maxAttempts = 4;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (!IsSpotifyHandoffCurrent(handoffToken) ||
                !await EnsureSpotifyPlaybackDeviceReadyAsync(showError: false) ||
                spotifyDeviceId is null)
            {
                return false;
            }

            await ActivateSpotifyPlaybackAsync(handoffToken);
            if (!IsSpotifyHandoffCurrent(handoffToken))
                return false;

            var deviceId = spotifyDeviceId;
            var started = await spotifyService.PlayUriAsync(playbackUri, SpotifyClientId, deviceId);
            LogSpotify($"play uri attempt {attempt}/{maxAttempts} deviceId={deviceId} started={started}");
            if (!IsSpotifyHandoffCurrent(handoffToken))
            {
                if (started)
                    _ = spotifyService.PauseAsync(SpotifyClientId, deviceId);
                return false;
            }

            if (started)
                return true;

            if (attempt == maxAttempts)
                break;

            if (attempt == 1)
            {
                var transferred = await spotifyService.TransferPlaybackAsync(deviceId, SpotifyClientId);
                LogSpotify($"play uri retry transfer deviceId={deviceId} transferred={transferred}");
                if (!IsSpotifyHandoffCurrent(handoffToken))
                    return false;
            }

            SetSpotifyStatus($"Retrying Spotify playback ({attempt + 1}/{maxAttempts})...");
            await Task.Delay(GetSpotifyPlaybackRetryDelay(attempt));
            if (!IsSpotifyHandoffCurrent(handoffToken))
                return false;
        }

        return false;
    }

    private static TimeSpan GetSpotifyPlaybackRetryDelay(int failedAttempt) =>
        TimeSpan.FromMilliseconds(failedAttempt switch
        {
            <= 1 => 600,
            2 => 1200,
            _ => 2000
        });

    private async Task RefreshSpotifyQueueAsync(bool force = false, long? handoffToken = null)
    {
        if (!spotifyService.IsLinked || spotifyQueueRefreshInProgress)
            return;

        var now = DateTime.UtcNow;
        if (!force && now - spotifyLastQueueRefreshUtc < TimeSpan.FromSeconds(5))
            return;

        spotifyQueueRefreshInProgress = true;
        spotifyLastQueueRefreshUtc = now;
        try
        {
            var snapshot = await spotifyService.GetQueueAsync(SpotifyClientId);
            if (handoffToken is { } token && !IsSpotifyHandoffCurrent(token))
                return;

            if (snapshot is null)
                return;

            var items = new List<QueueListItem>();
            var currentTrack = snapshot.Current;
            if (currentTrack is not null)
            {
                items.Add(new QueueListItem(
                    currentTrack.Name,
                    BuildSpotifyQueueSubtitle(currentTrack),
                    IsCurrent: true,
                    IsPlaying: spotifyIsPlaying));
            }
            else if (spotifyCurrentTrack is not null)
            {
                items.Add(new QueueListItem(
                    spotifyCurrentTrack.DisplayName,
                    BuildSpotifyQueueSubtitle(spotifyCurrentTrack.Artist, spotifyCurrentTrack.Album),
                    IsCurrent: true,
                    IsPlaying: spotifyIsPlaying));
            }

            foreach (var track in snapshot.Queue.Take(50))
            {
                items.Add(new QueueListItem(
                    track.Name,
                    BuildSpotifyQueueSubtitle(track),
                    IsCurrent: false,
                    IsPlaying: false));
            }

            spotifyQueueItems = items;
            if (isQueueVisible)
            {
                SyncQueueControl();
                RefreshContentColumns();
            }
        }
        finally
        {
            spotifyQueueRefreshInProgress = false;
        }
    }

    private static string? BuildSpotifyQueueSubtitle(SpotifyPlaybackTrack track) =>
        BuildSpotifyQueueSubtitle(track.Artist, track.Album);

    private static string? BuildSpotifyQueueSubtitle(string? artist, string? album)
    {
        if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album))
            return $"{artist} - {album}";

        if (!string.IsNullOrWhiteSpace(artist))
            return artist;

        return string.IsNullOrWhiteSpace(album) ? null : album;
    }

    private void DisposeSpotify()
    {
        StopSpotifyPlayback();
        spotifyWebView?.Dispose();
        spotifyWebView = null;
        spotifyService.Dispose();
    }

    private async Task ActivateSpotifyPlaybackAsync(long? handoffToken = null)
    {
        try
        {
            if (handoffToken is { } token && !IsSpotifyHandoffCurrent(token))
                return;

            if (spotifyWebView?.CoreWebView2 is not null)
                await spotifyWebView.CoreWebView2.ExecuteScriptAsync("window.spotifyActivate && window.spotifyActivate()");
        }
        catch { }
    }

    private async Task ResumeSpotifyPlaybackAsync(long? handoffToken = null)
    {
        try
        {
            if (handoffToken is { } token && !IsSpotifyHandoffCurrent(token))
                return;

            if (spotifyWebView?.CoreWebView2 is not null)
            {
                await spotifyWebView.CoreWebView2.ExecuteScriptAsync("window.spotifyResume && window.spotifyResume()");
                if (handoffToken is { } tokenAfterResume && !IsSpotifyHandoffCurrent(tokenAfterResume))
                    _ = spotifyService.PauseAsync(SpotifyClientId, spotifyDeviceId);
            }
        }
        catch { }
    }

    private async Task PullSpotifyStateAsync(long? handoffToken = null)
    {
        try
        {
            if (handoffToken is { } token && !IsSpotifyHandoffCurrent(token))
                return;

            if (spotifyWebView?.CoreWebView2 is not null)
                await spotifyWebView.CoreWebView2.ExecuteScriptAsync("window.spotifyPullState && window.spotifyPullState()");
        }
        catch { }
    }

    private async Task PollForSpotifyPlayingAsync(long handoffToken)
    {
        // Poll in short bursts — each iteration sends a state pull and then yields long enough
        // for the resulting web message to be processed on the UI thread before we re-check.
        for (var i = 0; i < 6 && !spotifyIsPlaying; i++)
        {
            if (!IsSpotifyHandoffCurrent(handoffToken))
                return;

            await Task.Delay(i == 0 ? 250 : 300);
            if (!IsSpotifyHandoffCurrent(handoffToken))
                return;

            if (!spotifyIsPlaying)
                await PullSpotifyStateAsync(handoffToken);
        }
    }

    private static async Task<byte[]?> FetchBytesAsync(string url, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient();
            return await http.GetByteArrayAsync(url, ct);
        }
        catch { return null; }
    }

    private void LogSpotify(string message)
    {
        try { AppLogPaths.AppendTimestamped(SpotifyLogPath, message); }
        catch { }
    }

    private void SetSpotifyStatus(string message)
    {
        LogSpotify($"status: {message}");
        spotifyStatusMessage = message;
        if (IsHandleCreated)
            BeginInvoke(UpdateUiState);
    }

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string GetSpotifyErrorStatus(JsonElement root)
    {
        var kind = root.TryGetProperty("kind", out var kindEl) ? kindEl.GetString() : null;
        var message = root.TryGetProperty("message", out var messageEl) ? messageEl.GetString() : null;

        return kind switch
        {
            "account_error" => "Spotify Premium is required",
            "authentication_error" => "Spotify needs to be linked again",
            "initialization_error" => "Spotify in-app playback unavailable",
            "autoplay_failed" => "Press Play Spotify in Spectralis to start playback",
            "playback_error" => string.IsNullOrWhiteSpace(message) ? "Spotify playback failed" : $"Spotify playback failed: {message}",
            _ => string.IsNullOrWhiteSpace(message) ? "Spotify player error" : $"Spotify: {message}"
        };
    }
}
