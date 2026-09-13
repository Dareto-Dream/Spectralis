using System.Text.Json;
using System.Text.Json.Nodes;
using Spectralis.Core.Capsule;
using Spectralis.Core.Platform;
using Spectralis.Core.Visualizers;

namespace Spectralis.Core.Integrations.Web;

public sealed class AlbumTrackPlayRequest
{
    public string TrackId { get; init; } = "";
    public double PositionSeconds { get; init; }
}

public sealed class AlbumBookmarkRequest
{
    public string TrackId { get; init; } = "";
    public double PositionSeconds { get; init; }
    public string Label { get; init; } = "";
}

/// <summary>
/// A capsule-supplied Discord rich presence override, sent from page JS via
/// <c>window.spectral.presence.set()</c>. Only honoured when the capsule declared the
/// <c>presence.richPresence</c> capability. All fields are clamped host-side before use.
/// </summary>
public sealed class CapsulePresenceRequest
{
    public string Details { get; init; } = "";
    public string State { get; init; } = "";
    public string LargeImageText { get; init; } = "";
    public string SmallImageText { get; init; } = "";
}

/// <summary>
/// A capsule-registered DSP preset, sent from page JS via <c>window.spectral.dsp.register()</c>.
/// <see cref="PresetChainJson"/> is the raw JSON object the page passed — same shape
/// <c>EffectChainState.Serialize</c> produces (<c>{"Enabled":bool,"Effects":[{"Name","Enabled","Params"}]}</c>) —
/// handed unmodified to <c>EffectChainState.Restore</c>, so a preset can only ever be built
/// from the app's existing effect factory. Only honoured when the capsule declared the
/// <c>audio.dspPreset</c> capability.
/// </summary>
public sealed class WorldDspPresetRequest
{
    public string PresetChainJson { get; init; } = "";
}

/// <summary>
/// Drives an <see cref="IWebViewHost"/> for capsule/album-world content: the
/// spectral.* JS bridge, window.spectral v5 bootstrap, audio frame push, CSP
/// injection, and per-capsule persistent store. All page input is untrusted:
/// messages are size-capped, schema-checked, and anything unrecognized is dropped.
/// </summary>
public sealed class WebViewHostService : IDisposable
{
    private const int MaxMessageBytes = 64 * 1024;

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const int MaxPresenceTextLength = 128;

    private readonly IWebViewHost _host;
    private readonly CapsuleScopedStore? _scopedStore;
    private readonly bool _isAlbumWorld;
    private readonly bool _allowPresence;
    private readonly bool _allowDspPreset;

    /// <param name="storeKey">
    /// Capsule identifier used for per-capsule persistent storage.
    /// Pass null to disable the store (album worlds, OBS overlays, etc.)
    /// </param>
    /// <param name="isAlbumWorld">
    /// True when hosting a .spectral album world map. Enables world-specific bridge
    /// messages (playTrack, addToQueue) and the corresponding JS callbacks.
    /// </param>
    /// <param name="allowPresence">
    /// True when the hosted capsule declared the <c>presence.richPresence</c> capability.
    /// Gates the <c>spectral.presence.*</c> bridge messages; page JS can always call the
    /// stub, but the host drops the messages unless this is set.
    /// </param>
    /// <param name="allowDspPreset">
    /// True when the hosted capsule declared the <c>audio.dspPreset</c> capability.
    /// Gates the <c>spectral.dsp.*</c> bridge messages the same way <paramref name="allowPresence"/>
    /// gates presence.
    /// </param>
    public WebViewHostService(
        IWebViewHost host,
        string? storeKey = null,
        bool isAlbumWorld = false,
        bool allowPresence = false,
        bool allowDspPreset = false)
    {
        _host = host;
        _host.MessageReceived += OnMessageReceived;
        _isAlbumWorld = isAlbumWorld;
        _allowPresence = allowPresence;
        _allowDspPreset = allowDspPreset;

        if (!string.IsNullOrWhiteSpace(storeKey))
        {
            _scopedStore = new CapsuleScopedStore(storeKey);
        }
    }

    public event EventHandler<AlbumTrackPlayRequest>? PlayTrackRequested;
    public event EventHandler<string>? AddToQueueRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? ResumeRequested;
    public event EventHandler<double>? SeekRequested;
    public event EventHandler<AlbumBookmarkRequest>? SaveBookmarkRequested;
    public event EventHandler? ExitWorldRequested;
    public event EventHandler<CapsulePresenceRequest>? PresenceUpdateRequested;
    public event EventHandler? PresenceClearRequested;
    public event EventHandler<WorldDspPresetRequest>? DspPresetRegisterRequested;
    public event EventHandler? DspPresetReleaseRequested;

    private void OnMessageReceived(object? sender, string messageJson) => DispatchMessage(messageJson);

    /// <summary>Validates and dispatches one bridge message. Public for tests.</summary>
    public void DispatchMessage(string messageJson)
    {
        if (string.IsNullOrWhiteSpace(messageJson) || messageJson.Length > MaxMessageBytes)
            return;

        try
        {
            using var doc = JsonDocument.Parse(messageJson, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeProp))
                return;

            switch (typeProp.GetString() ?? string.Empty)
            {
                case "spectral.playTrack":
                {
                    // Album world only — ignored when hosting a regular HTML visualizer.
                    if (!_isAlbumWorld) break;
                    if (!root.TryGetProperty("trackId", out var idProp) || idProp.ValueKind != JsonValueKind.String)
                        return;

                    var position = root.TryGetProperty("positionSeconds", out var posProp) &&
                                   posProp.ValueKind == JsonValueKind.Number
                        ? posProp.GetDouble()
                        : 0.0;
                    if (!double.IsFinite(position) || position < 0)
                        position = 0;

                    PlayTrackRequested?.Invoke(this, new AlbumTrackPlayRequest
                    {
                        TrackId = idProp.GetString() ?? string.Empty,
                        PositionSeconds = position,
                    });
                    break;
                }

                case "spectral.addToQueue":
                    // Album world only — ignored when hosting a regular HTML visualizer.
                    if (_isAlbumWorld &&
                        root.TryGetProperty("trackId", out var queueIdProp) &&
                        queueIdProp.ValueKind == JsonValueKind.String)
                        AddToQueueRequested?.Invoke(this, queueIdProp.GetString() ?? string.Empty);
                    break;

                case "spectral.pause":
                    PauseRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case "spectral.resume":
                    ResumeRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case "spectral.seek":
                    if (root.TryGetProperty("positionSeconds", out var seekPosProp) &&
                        seekPosProp.ValueKind == JsonValueKind.Number &&
                        double.IsFinite(seekPosProp.GetDouble()) &&
                        seekPosProp.GetDouble() >= 0)
                    {
                        SeekRequested?.Invoke(this, seekPosProp.GetDouble());
                    }
                    break;

                case "spectral.saveBookmark":
                {
                    var trackId = root.TryGetProperty("trackId", out var bmIdProp) && bmIdProp.ValueKind == JsonValueKind.String
                        ? bmIdProp.GetString() ?? string.Empty : string.Empty;
                    var pos = root.TryGetProperty("positionSeconds", out var bmPosProp) &&
                              bmPosProp.ValueKind == JsonValueKind.Number
                        ? bmPosProp.GetDouble() : 0;
                    var label = root.TryGetProperty("label", out var lblProp) && lblProp.ValueKind == JsonValueKind.String
                        ? lblProp.GetString() ?? string.Empty : string.Empty;

                    SaveBookmarkRequested?.Invoke(this, new AlbumBookmarkRequest
                    {
                        TrackId = trackId,
                        PositionSeconds = double.IsFinite(pos) && pos >= 0 ? pos : 0,
                        Label = label.Length > 256 ? label[..256] : label,
                    });
                    break;
                }

                case "spectral.exitWorld":
                    ExitWorldRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case "spectral.presence.set":
                {
                    // Gated on the presence.richPresence capability — dropped otherwise.
                    if (!_allowPresence) break;

                    var details = ClampPresenceText(ReadString(root, "details"));
                    var state = ClampPresenceText(ReadString(root, "state"));
                    if (details.Length == 0 && state.Length == 0) break;

                    PresenceUpdateRequested?.Invoke(this, new CapsulePresenceRequest
                    {
                        Details = details,
                        State = state,
                        LargeImageText = ClampPresenceText(ReadString(root, "largeImageText")),
                        SmallImageText = ClampPresenceText(ReadString(root, "smallImageText")),
                    });
                    break;
                }

                case "spectral.presence.clear":
                    if (_allowPresence)
                        PresenceClearRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case "spectral.registerDspPreset":
                {
                    // Gated on the audio.dspPreset capability — dropped otherwise.
                    if (!_allowDspPreset) break;
                    if (!root.TryGetProperty("preset", out var presetProp) ||
                        presetProp.ValueKind != JsonValueKind.Object)
                        break;

                    var presetJson = presetProp.GetRawText();
                    if (presetJson.Length > MaxMessageBytes) break;

                    DspPresetRegisterRequested?.Invoke(this, new WorldDspPresetRequest
                    {
                        PresetChainJson = presetJson,
                    });
                    break;
                }

                case "spectral.releaseDspPreset":
                    if (_allowDspPreset)
                        DspPresetReleaseRequested?.Invoke(this, EventArgs.Empty);
                    break;

                // Per-capsule persistent store
                case "spectral.store.get":
                    HandleStoreGet(root);
                    break;

                case "spectral.store.set":
                    HandleStoreSet(root);
                    break;

                case "spectral.store.remove":
                    HandleStoreRemove(root);
                    break;

                case "spectral.store.clear":
                    HandleStoreClear();
                    break;
            }
        }
        catch (JsonException)
        {
            // Malformed page input is dropped, never fatal.
        }
    }

    // ===== host-bound pushes =====

    public Task InjectBootstrapAsync() => _host.ExecuteScriptAsync(BuildBootstrapScript(_isAlbumWorld));

    public Task SendReadyAsync(string worldStateJson) =>
        _host.ExecuteScriptAsync(
            $"if (typeof window.spectral?.onReady === 'function') window.spectral.onReady({worldStateJson});");

    public Task SendTrackChangedAsync(string trackId, string title, string artist, double durationSeconds)
    {
        var json = JsonSerializer.Serialize(
            new { id = trackId, title, artist, durationSeconds }, SerializeOptions);
        return _host.ExecuteScriptAsync(
            $"if (typeof window.spectral?.onTrackChanged === 'function') window.spectral.onTrackChanged({json});");
    }

    public Task SendTrackCompletedAsync(string trackId, double playedSeconds)
    {
        var json = JsonSerializer.Serialize(new { trackId, playedSeconds }, SerializeOptions);
        return _host.ExecuteScriptAsync(
            $"if (typeof window.spectral?.onTrackCompleted === 'function') window.spectral.onTrackCompleted({json});");
    }

    public Task PushFrameAsync(VisualizerFrame frame, bool playing, float position, string currentTrackId = "")
    {
        var json = JsonSerializer.Serialize(new
        {
            levels = SampleSpectrum(frame.Spectrum, 64),
            peak = Math.Clamp(frame.PeakLevel, 0f, 1.25f),
            rms = Math.Clamp(frame.RmsLevel, 0f, 1.25f),
            active = playing,
            time = (double)position,
            trackId = currentTrackId,
        }, SerializeOptions);

        // Update _lastFrame cache so spectral.getFrame() works on WebView2 too;
        // also fire onPlaybackFrame which the new event system listens to.
        return _host.ExecuteScriptAsync(
            $"(function(f){{" +
            $"window.spectral._lastFrame=f;" +
            $"if(typeof window.spectral?.onPlaybackFrame==='function')window.spectral.onPlaybackFrame(f);" +
            $"}})({json});");
    }

    // ===== bootstrap =====

    /// <summary>
    /// Builds the window.spectral v5 bootstrap script injected into every capsule.
    ///
    /// API surface:
    ///   spectral.meta          — track metadata (title, artist, album, artwork, bpm, key, duration …)
    ///   spectral.getFrame()    — synchronous pull; works on CefGlue natively, falls back to last
    ///                           pushed frame on WebView2
    ///   spectral.on(evt, fn)   — subscribe to events: 'frame', 'ready', 'trackChange',
    ///                           'trackComplete', 'seek', 'stateChange', 'sessionRestored'
    ///   spectral.off(evt, fn)  — unsubscribe
    ///   spectral.store.*       — persistent per-capsule key-value store (async get, sync set)
    ///   spectral.pause()       — control playback
    ///   spectral.resume()
    ///   spectral.seek(sec)
    ///   spectral.exit()
    ///   spectral.presence.*    — Discord rich presence override (presence.richPresence capability)
    ///   spectral.dsp.*         — register/release a whole-rack DSP preset (audio.dspPreset capability)
    ///
    ///   CSS custom properties on <html>:
    ///     --audio-time, --audio-peak, --audio-rms  (set by embedded frame bridge, not here)
    ///     --spectral-duration, --spectral-progress  (set by embedded frame bridge)
    ///
    /// This script is intentionally framework-agnostic vanilla JS.
    /// </summary>
    /// <summary>
    /// Builds the window.spectral bootstrap for the given surface mode.
    /// HTML visualizers get the frame/playback/store/meta API.
    /// Album worlds additionally get track-navigation callbacks (onReady, onTrackChanged, etc.)
    /// that don't belong in a regular per-track HTML visualizer context.
    /// </summary>
    public static string BuildBootstrapScript(bool isAlbumWorld = false)
    {
        const string core = """
            (function() {
              if (window.__spectralBootstrapped) return;
              window.__spectralBootstrapped = true;

              window.spectral = window.spectral || {};

              // ── Event system ──────────────────────────────────────────────────
              var _h = {};
              window.spectral.on = function(event, fn) {
                (_h[event] = _h[event] || []).push(fn);
                return window.spectral;
              };
              window.spectral.off = function(event, fn) {
                var arr = _h[event];
                if (arr) { var i = arr.indexOf(fn); if (i >= 0) arr.splice(i, 1); }
                return window.spectral;
              };
              window.spectral._emit = function(event, data) {
                var arr = _h[event] || [];
                for (var i = 0; i < arr.length; i++) try { arr[i](data); } catch {}
              };

              // ── Frame callback (shared: HTML visualizers and album worlds both use this) ──
              window.spectral.onPlaybackFrame = window.spectral.onPlaybackFrame ||
                function(d) {
                  window.spectral._lastFrame = d;
                  window.spectral._emit('frame', d);
                };

              // ── Pull frame API ────────────────────────────────────────────────
              // On CefGlue: spectralisBridge.getFrameJson() is a live C# call.
              // On WebView2: returns '' — falls back to last pushed frame in _lastFrame.
              window.spectral._lastFrame = null;
              window.spectral.getFrame = function() {
                try {
                  var raw = spectralisBridge.getFrameJson();
                  if (raw) return JSON.parse(raw);
                } catch {}
                return window.spectral._lastFrame || null;
              };

              // ── Playback controls ─────────────────────────────────────────────
              window.spectral.pause = function() {
                spectralisBridge.postMessage(JSON.stringify({ type: 'spectral.pause' }));
              };
              window.spectral.resume = function() {
                spectralisBridge.postMessage(JSON.stringify({ type: 'spectral.resume' }));
              };
              window.spectral.seek = function(sec) {
                spectralisBridge.postMessage(JSON.stringify({ type: 'spectral.seek', positionSeconds: Number(sec) || 0 }));
              };
              window.spectral.exit = function() {
                spectralisBridge.postMessage(JSON.stringify({ type: 'spectral.exitWorld' }));
              };

              // ── Discord rich presence ────────────────────────────────────────
              // Needs the presence.richPresence capability; calls are dropped host-side otherwise.
              // presence.set({ details, state, largeImageText, smallImageText }) — all optional strings.
              // presence.clear() reverts to the normal track presence.
              window.spectral.presence = {
                set: function(p) {
                  p = p || {};
                  spectralisBridge.postMessage(JSON.stringify({
                    type: 'spectral.presence.set',
                    details: String(p.details || ''),
                    state: String(p.state || ''),
                    largeImageText: String(p.largeImageText || ''),
                    smallImageText: String(p.smallImageText || '')
                  }));
                },
                clear: function() {
                  spectralisBridge.postMessage(JSON.stringify({ type: 'spectral.presence.clear' }));
                }
              };

              // ── DSP preset control ───────────────────────────────────────────
              // Needs the audio.dspPreset capability; calls are dropped host-side otherwise.
              // register(preset) — preset is {Enabled, Effects:[{Name, Enabled, Params}]},
              // the same shape the app's own saved chain presets use. Only effect names the
              // app already knows how to build are honoured; anything else is skipped.
              // Auto-reverts to the user's own chain on release() or when the surface unloads.
              window.spectral.dsp = {
                register: function(preset) {
                  spectralisBridge.postMessage(JSON.stringify({
                    type: 'spectral.registerDspPreset',
                    preset: preset || {}
                  }));
                },
                release: function() {
                  spectralisBridge.postMessage(JSON.stringify({ type: 'spectral.releaseDspPreset' }));
                }
              };

              // ── Persistent store ──────────────────────────────────────────────
              // set/remove/clear are fire-and-forget. get returns a Promise.
              // The store is per-capsule; disabled when no storeKey was configured.
              (function() {
                var _pending = {};
                window.spectral.store = {
                  get: function(key) {
                    return new Promise(function(resolve) {
                      var id = String(Date.now()) + Math.random().toString(36).slice(2);
                      _pending[id] = resolve;
                      spectralisBridge.postMessage(JSON.stringify({
                        type: 'spectral.store.get', key: String(key), requestId: id
                      }));
                    });
                  },
                  set: function(key, value) {
                    spectralisBridge.postMessage(JSON.stringify({
                      type: 'spectral.store.set',
                      key: String(key),
                      value: value === undefined ? null : value
                    }));
                  },
                  remove: function(key) {
                    spectralisBridge.postMessage(JSON.stringify({
                      type: 'spectral.store.remove', key: String(key)
                    }));
                  },
                  clear: function() {
                    spectralisBridge.postMessage(JSON.stringify({ type: 'spectral.store.clear' }));
                  }
                };
                // C# calls this to resolve pending get() promises.
                window.spectral._storeResult = function(id, value) {
                  var cb = _pending[id];
                  if (cb) { delete _pending[id]; cb(value); }
                };
              })();

              // meta is populated by InjectTrackMeta at document-build time (HTML visualizers only).
              window.spectral.meta = window.spectral.meta || null;
            })();
            """;

        if (!isAlbumWorld) return core;

        // Album world extensions: track-navigation callbacks not exposed to regular HTML visualizers.
        const string worldExt = """
            (function() {
              window.spectral = window.spectral || {};
              // ── Album world callbacks ─────────────────────────────────────────
              window.spectral.onReady = window.spectral.onReady ||
                function(d) { window.spectral._emit('ready', d); };
              window.spectral.onTrackChanged = window.spectral.onTrackChanged ||
                function(d) { window.spectral._emit('trackChange', d); };
              window.spectral.onTrackCompleted = window.spectral.onTrackCompleted ||
                function(d) { window.spectral._emit('trackComplete', d); };
              window.spectral.onSessionRestored = window.spectral.onSessionRestored ||
                function(d) { window.spectral._emit('sessionRestored', d); };
            })();
            """;

        return core + "\n" + worldExt;
    }

    // ===== CSP =====

    public static string BuildContentSecurityPolicy(bool allowNetworkAccess) =>
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; " +
        "media-src 'self' blob:; " +
        "font-src 'self' data:; " +
        (allowNetworkAccess ? "connect-src https:; " : "connect-src 'none'; ") +
        "object-src 'none'; frame-src 'none'; base-uri 'none'; form-action 'none'";

    public static string InjectContentSecurityPolicy(string html, bool allowNetworkAccess)
    {
        var meta = $"<meta http-equiv=\"Content-Security-Policy\" content=\"{BuildContentSecurityPolicy(allowNetworkAccess)}\">";

        var headIndex = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (headIndex >= 0)
        {
            var headClose = html.IndexOf('>', headIndex);
            if (headClose >= 0)
                return html.Insert(headClose + 1, meta);
        }

        var htmlIndex = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        if (htmlIndex >= 0)
        {
            var htmlClose = html.IndexOf('>', htmlIndex);
            if (htmlClose >= 0)
                return html.Insert(htmlClose + 1, $"<head>{meta}</head>");
        }

        return $"<head>{meta}</head>{html}";
    }

    // ===== store internals =====

    private void HandleStoreGet(JsonElement root)
    {
        if (_scopedStore is null) return;
        if (!root.TryGetProperty("key", out var keyProp) || keyProp.ValueKind != JsonValueKind.String) return;
        if (!root.TryGetProperty("requestId", out var idProp) || idProp.ValueKind != JsonValueKind.String) return;

        var key = keyProp.GetString() ?? string.Empty;
        var requestId = idProp.GetString() ?? string.Empty;
        if (key.Length > CapsuleScopedStore.MaxKeyBytes || requestId.Length > 128) return;

        var value = _scopedStore.Get(key);
        var valueJson = value is null ? "null" : value.ToJsonString();
        var safeId = JsonSerializer.Serialize(requestId);

        _ = _host.ExecuteScriptAsync($"window.spectral._storeResult({safeId},{valueJson});");
    }

    private void HandleStoreSet(JsonElement root)
    {
        if (_scopedStore is null) return;
        if (!root.TryGetProperty("key", out var keyProp) || keyProp.ValueKind != JsonValueKind.String) return;

        var key = keyProp.GetString() ?? string.Empty;
        if (key.Length > CapsuleScopedStore.MaxKeyBytes) return;

        if (root.TryGetProperty("value", out var valueProp))
        {
            var valueJson = valueProp.GetRawText();
            if (valueJson.Length > CapsuleScopedStore.MaxValueBytes) return;
            _scopedStore.Set(key, JsonNode.Parse(valueJson));
        }
        else
        {
            _scopedStore.Set(key, null);
        }
    }

    private void HandleStoreRemove(JsonElement root)
    {
        if (_scopedStore is null) return;
        if (!root.TryGetProperty("key", out var keyProp) || keyProp.ValueKind != JsonValueKind.String) return;

        var key = keyProp.GetString() ?? string.Empty;
        if (key.Length > CapsuleScopedStore.MaxKeyBytes) return;

        _scopedStore.Remove(key);
    }

    private void HandleStoreClear()
    {
        _scopedStore?.Clear();
    }

    // ===== helpers =====

    private static float[] SampleSpectrum(float[] spectrum, int count)
    {
        if (spectrum.Length == 0)
            return new float[count];

        var result = new float[count];
        var ratio = (double)spectrum.Length / count;
        for (var i = 0; i < count; i++)
        {
            var src = (int)(i * ratio);
            result[i] = Math.Clamp(spectrum[Math.Min(src, spectrum.Length - 1)], 0, 1.25f);
        }

        return result;
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? string.Empty
            : string.Empty;

    private static string ClampPresenceText(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length > MaxPresenceTextLength ? text[..MaxPresenceTextLength] : text;
    }

    public void Dispose() => _host.MessageReceived -= OnMessageReceived;
}
