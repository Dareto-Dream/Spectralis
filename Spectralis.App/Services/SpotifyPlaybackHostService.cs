using System.IO;
using System.Text.Json;
using Spectralis.Core.Common;
using Spectralis.Core.Integrations.Spotify;
using Spectralis.Core.Platform;

namespace Spectralis.App.Services;

public record SpotifyTrackState(
    string TrackId,
    string Name,
    string Artist,
    string Album,
    string? AlbumArtUrl,
    bool IsPaused,
    double PositionMs,
    double DurationMs);

/// <summary>
/// Hosts the Spotify Web Playback SDK in a hidden WebView (the same
/// <see cref="IWebViewHost"/> abstraction used for embedded track HTML) so
/// Spectralis can register itself as a Spotify Connect device and transfer
/// the user's existing Spotify session onto it. Ports the legacy WinForms
/// app's Form1.Spotify.cs device-ready/transfer/resume flow.
/// </summary>
public sealed class SpotifyPlaybackHostService
{
    private static readonly TimeSpan DeviceReadyTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan DeviceReadyPollInterval = TimeSpan.FromMilliseconds(200);

    // Virtual hostname for the Spotify player HTML served over HTTPS.
    // The Web Playback SDK requires window.isSecureContext === true, which
    // NavigateToString() cannot provide — a virtual host URL gives us that.
    private const string SpotifyWebPlayerHost = "spectralis.spotify.local";

    public static string SpotifyLogPath => AppLogPaths.For("spotify.log");

    private readonly IWebViewHost _host;
    private readonly SpotifyService _spotify;
    private readonly Func<string> _resolveClientId;
    private string? _deviceId;
    private bool _navigated;
    private string? _statusMessage;
    private bool _initError;

    public SpotifyPlaybackHostService(IWebViewHost host, SpotifyService spotify, Func<string> resolveClientId)
    {
        _host = host;
        _spotify = spotify;
        _resolveClientId = resolveClientId;
        _host.MessageReceived += OnMessageReceived;
    }

    public bool IsDeviceReady => _deviceId is not null;
    public string? DeviceId => _deviceId;

    public string? StatusMessage => _statusMessage;

    /// <summary>Fired on every player_state_changed event from the SDK with track + playback state.</summary>
    public event EventHandler<SpotifyTrackState>? TrackStateChanged;

    public Task<bool> PauseAsync()
        => _deviceId is not null ? _spotify.PauseAsync(_resolveClientId(), _deviceId) : Task.FromResult(false);

    public Task<bool> ResumeAsync()
        => _deviceId is not null ? _spotify.ResumeAsync(_resolveClientId(), _deviceId) : Task.FromResult(false);

    public Task<bool> NextTrackAsync()
        => _deviceId is not null ? _spotify.NextTrackAsync(_resolveClientId(), _deviceId) : Task.FromResult(false);

    public Task<bool> PreviousTrackAsync()
        => _deviceId is not null ? _spotify.PreviousTrackAsync(_resolveClientId(), _deviceId) : Task.FromResult(false);

    public Task<bool> SeekAsync(int positionMs)
        => _deviceId is not null ? _spotify.SeekAsync(positionMs, _resolveClientId(), _deviceId) : Task.FromResult(false);

    public async Task StopAsync()
    {
        if (_deviceId is not null)
            await _spotify.PauseAsync(_resolveClientId(), _deviceId);
        _deviceId = null;
        _statusMessage = null;
    }

    public Task<SpotifyQueueSnapshot?> GetQueueAsync()
        => _spotify.GetQueueAsync(_resolveClientId());

    /// <summary>
    /// Ensures the SDK device is registered and ready, then transfers the
    /// user's Spotify session onto it and resumes playback — the full
    /// "Play Spotify" flow.
    /// </summary>
    public async Task<bool> PlayAsync()
    {
        AppLogPaths.AppendTimestamped(SpotifyLogPath, "PlayAsync: ensuring device ready");
        if (!await EnsureDeviceReadyAsync())
        {
            AppLogPaths.AppendTimestamped(SpotifyLogPath, $"PlayAsync: device not ready — {_statusMessage}");
            return false;
        }

        await _host.ExecuteScriptAsync("window.spotifyActivate && window.spotifyActivate()");

        var clientId = _resolveClientId();
        var transferred = await _spotify.TransferPlaybackAsync(_deviceId!, clientId);
        if (!transferred)
        {
            _statusMessage = "Spotify transfer failed";
            AppLogPaths.AppendTimestamped(SpotifyLogPath, "PlayAsync: transfer failed");
            return false;
        }

        await Task.Delay(500);
        await _host.ExecuteScriptAsync("window.spotifyResume && window.spotifyResume()");
        _statusMessage = "Spotify playback requested";
        AppLogPaths.AppendTimestamped(SpotifyLogPath, "PlayAsync: playback requested");
        return true;
    }

    public async Task<bool> EnsureDeviceReadyAsync()
    {
        if (_deviceId is not null)
            return true;

        // Allow re-attempt after a previous init error
        if (_initError)
        {
            _initError = false;
            _navigated = false;
            _statusMessage = null;
        }

        if (!_navigated)
            await NavigateAsync();

        if (_deviceId is not null)
            return true;

        // Navigation may have already set an error — don't overwrite it
        if (_statusMessage is not null)
            return false;

        _statusMessage = "Waiting for Spotify player...";
        var deadlineUtc = DateTime.UtcNow + DeviceReadyTimeout;
        while (DateTime.UtcNow < deadlineUtc)
        {
            if (_deviceId is not null)
                return true;
            if (_initError)
                return false;
            await Task.Delay(DeviceReadyPollInterval);
        }

        _statusMessage ??= "Spotify is not ready yet. Try again in a few seconds.";
        return _deviceId is not null;
    }

    private async Task NavigateAsync()
    {
        _navigated = true;
        _initError = false;
        var clientId = _resolveClientId();
        if (string.IsNullOrEmpty(clientId))
        {
            _statusMessage = "No Spotify client ID — add one in Settings → Integrations";
            AppLogPaths.AppendTimestamped(SpotifyLogPath, "Navigate: no client ID configured");
            return;
        }

        var token = await _spotify.GetFreshAccessTokenAsync(clientId);
        if (token is null)
        {
            _statusMessage = "Spotify token expired — re-link in Settings → Integrations";
            AppLogPaths.AppendTimestamped(SpotifyLogPath, "Navigate: token expired or missing");
            return;
        }

        // Write the player HTML to a temp file served via virtual host so the page runs
        // in a secure context (https://spectralis.spotify.local/), which is required by
        // the Spotify Web Playback SDK. NavigateToString() is non-secure and SDK fails.
        var webPlayerDir = Path.Combine(Path.GetTempPath(), "spectralis-spotify-web-player");
        Directory.CreateDirectory(webPlayerDir);
        var playerHtmlPath = Path.Combine(webPlayerDir, "player.html");
        await File.WriteAllTextAsync(playerHtmlPath, BuildPlayerHtml(token));

        _host.MapVirtualHost(SpotifyWebPlayerHost, webPlayerDir);

        var url = new Uri($"https://{SpotifyWebPlayerHost}/player.html?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        AppLogPaths.AppendTimestamped(SpotifyLogPath, $"Navigate: loading Web Playback SDK at {url}");
        _host.Navigate(url);
    }

    private void OnMessageReceived(object? sender, string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

            switch (type)
            {
                case "ready":
                    _deviceId = root.TryGetProperty("deviceId", out var deviceIdEl) ? deviceIdEl.GetString() : null;
                    _statusMessage = "Spotify ready";
                    AppLogPaths.AppendTimestamped(SpotifyLogPath, $"Device ready: {_deviceId}");
                    break;
                case "not_ready":
                    var offlineDeviceId = root.TryGetProperty("deviceId", out var offlineEl) ? offlineEl.GetString() : null;
                    if (offlineDeviceId == _deviceId)
                        _deviceId = null;
                    _statusMessage = "Spotify device went offline";
                    AppLogPaths.AppendTimestamped(SpotifyLogPath, $"Device offline: {offlineDeviceId}");
                    break;
                case "connect_result":
                    var success = root.TryGetProperty("success", out var successEl) && successEl.GetBoolean();
                    AppLogPaths.AppendTimestamped(SpotifyLogPath, $"connect_result success={success}");
                    break;
                case "state_changed":
                    if (root.TryGetProperty("track", out var tEl))
                    {
                        var trackId     = tEl.TryGetProperty("id",           out var idEl)  ? idEl.GetString()  ?? "" : "";
                        var trackName   = tEl.TryGetProperty("name",        out var nEl)  ? nEl.GetString()  ?? "" : "";
                        var trackArtist = tEl.TryGetProperty("artist",      out var aEl)  ? aEl.GetString()  ?? "" : "";
                        var trackAlbum  = tEl.TryGetProperty("album",       out var alEl) ? alEl.GetString() ?? "" : "";
                        var artUrl      = tEl.TryGetProperty("albumArtUrl", out var uEl)  && uEl.ValueKind == JsonValueKind.String ? uEl.GetString() : null;
                        var isPaused    = root.TryGetProperty("isPaused",   out var pEl)  && pEl.GetBoolean();
                        var posMs       = root.TryGetProperty("position",   out var posEl) ? posEl.GetDouble() : 0.0;
                        var durMs       = root.TryGetProperty("duration",   out var durEl) ? durEl.GetDouble() : 0.0;
                        TrackStateChanged?.Invoke(this, new SpotifyTrackState(trackId, trackName, trackArtist, trackAlbum, artUrl, isPaused, posMs, durMs));
                        AppLogPaths.AppendTimestamped(SpotifyLogPath, $"state_changed track={trackName} paused={isPaused}");
                    }
                    break;
                case "token_request":
                    AppLogPaths.AppendTimestamped(SpotifyLogPath, "token_request received");
                    _ = FulfillTokenRequestAsync();
                    break;
                case "log":
                    var logMessage = root.TryGetProperty("message", out var logEl) ? logEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(logMessage))
                    {
                        SpectralisLog.Info($"[Spotify SDK] {logMessage}");
                        AppLogPaths.AppendTimestamped(SpotifyLogPath, $"[SDK] {logMessage}");
                    }
                    break;
                case "error":
                    var errorKind = root.TryGetProperty("kind", out var kindEl) ? kindEl.GetString() : null;
                    var errorMessage = root.TryGetProperty("message", out var errEl) ? errEl.GetString() : null;
                    SpectralisLog.Warn($"Spotify SDK error [{errorKind}]: {errorMessage}");
                    AppLogPaths.AppendTimestamped(SpotifyLogPath, $"[SDK error] kind={errorKind} message={errorMessage}");
                    _statusMessage = errorKind switch
                    {
                        "initialization_error" => "Spotify in-app playback unavailable",
                        "authentication_error" => "Spotify needs to be linked again — re-link in Settings → Integrations",
                        "account_error"        => "Spotify Premium is required",
                        "autoplay_failed"      => "Press Play Spotify to start playback",
                        _                      => string.IsNullOrWhiteSpace(errorMessage) ? "Spotify player error" : $"Spotify: {errorMessage}"
                    };
                    _initError = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            SpectralisLog.Warn($"Spotify SDK message parse failed: {ex.Message}");
            AppLogPaths.AppendTimestamped(SpotifyLogPath, $"Message parse failed: {ex.Message}");
        }
    }

    private async Task FulfillTokenRequestAsync()
    {
        try
        {
            var clientId = _resolveClientId();
            var fresh = await _spotify.GetFreshAccessTokenAsync(clientId);
            if (fresh is not null)
            {
                await _host.ExecuteScriptAsync($"window.provideToken({JsonSerializer.Serialize(fresh)})");
                AppLogPaths.AppendTimestamped(SpotifyLogPath, "token_request fulfilled");
            }
            else
            {
                AppLogPaths.AppendTimestamped(SpotifyLogPath, "token_request failed: no fresh token");
            }
        }
        catch (Exception ex)
        {
            AppLogPaths.AppendTimestamped(SpotifyLogPath, $"token_request exception: {ex.Message}");
        }
    }

    private static string BuildPlayerHtml(string accessToken)
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
                        albumArtUrl: t.album.images.length > 0 ? t.album.images[0].url : null
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
                    log('Spotify player ready device_id=' + device_id);
                    post({type:'ready', deviceId:device_id});
                });
                player.addListener('not_ready', ({device_id}) => {
                    log('Spotify player not_ready device_id=' + device_id);
                    post({type:'not_ready', deviceId:device_id});
                });
                player.addListener('initialization_error', ({message}) => postError('initialization_error', message));
                player.addListener('authentication_error', ({message}) => postError('authentication_error', message));
                player.addListener('account_error', ({message}) => postError('account_error', message));
                player.addListener('playback_error', ({message}) => postError('playback_error', message));
                player.addListener('autoplay_failed', () => postError('autoplay_failed', 'Autoplay blocked'));
                player.addListener('player_state_changed', state => postState(state));
                window.spotifyPlayer = player;
                player.connect().then(success => {
                    log('player.connect() resolved: ' + success);
                    post({type:'connect_result', success});
                }).catch(e => postError('connect_error', e && e.message));
            };
            window.spotifyActivate = async () => {
                try {
                    if (window.spotifyPlayer && window.spotifyPlayer.activateElement)
                        await window.spotifyPlayer.activateElement();
                } catch (e) { postError('activate_error', e && e.message); }
            };
            window.spotifyResume = async () => {
                try {
                    if (window.spotifyPlayer && window.spotifyPlayer.resume)
                        await window.spotifyPlayer.resume();
                } catch (e) { postError('resume_error', e && e.message); }
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
}
