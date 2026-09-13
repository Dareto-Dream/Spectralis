using System.Text;
using Spectralis.Core.Capsule;
using Wasmtime;
using AlbumTrackPlayRequest = Spectralis.Core.Integrations.Web.AlbumTrackPlayRequest;
using AlbumBookmarkRequest = Spectralis.Core.Integrations.Web.AlbumBookmarkRequest;
using WorldDspPresetRequest = Spectralis.Core.Integrations.Web.WorldDspPresetRequest;

namespace Spectralis.Core.Worlds;

/// <summary>
/// Sandboxed host for a single Wasm "album world" module (Phase 2 of the dual-runtime rework —
/// see docs/formats/spectral-album-world.md for the HTML-mode sibling this mirrors).
///
/// Host-import surface (module "spectral", mirroring <c>window.spectral.*</c> on the HTML side):
///   play_track(ptr,len,pos: f64)                          — same shape as spectral.playTrack
///   add_to_queue(ptr,len)                                 — spectral.addToQueue
///   save_bookmark(trackIdPtr,trackIdLen,pos: f64,labelPtr,labelLen) — spectral.saveBookmark
///   register_dsp_preset(ptr,len)              — spectral.dsp.register (audio.dspPreset capability)
///   release_dsp_preset()                      — spectral.dsp.release
///   unlock_achievement(ptr,len)
///   switch_to_html(ptr,len)                   — request a hand-off to HTML mode (symmetric with
///                                                the HTML side's spectral.worlds.switchToWasm)
///   storage_get(keyPtr,keyLen,outPtr,outCap) -> i32 bytes written (-1 = not found/buffer too small)
///   storage_set(keyPtr,keyLen,valPtr,valLen)
///
/// storage_get/storage_set share the exact same <see cref="CapsuleScopedStore"/> file the HTML
/// bridge's spectral.store.* uses for the same world id — this is what lets hand-off state
/// (position, achievements, active DSP preset id, ...) survive a runtime switch.
///
/// Untrusted content is fuel-limited (<see cref="Config.WithFuelConsumption"/>), mirroring the
/// non-fatal sandboxing philosophy already used for the Jint scripted-visualizer runtime
/// (Spectralis.Core/Visualizers/Scripting/JsVisualizerRuntime.cs): a trap (fuel exhaustion or a
/// guest panic) drops that call, never crashes the host.
/// </summary>
public sealed class WasmWorldHost : IDisposable
{
    /// <summary>Fuel budget re-topped before every host-to-guest call (on_load/on_tick/on_unload).</summary>
    public const long FuelBudgetPerCall = 10_000_000;

    private const int MaxStringBytes = 4096;

    private readonly Engine _engine;
    private readonly Store _store;
    private readonly Linker _linker;
    private readonly CapsuleScopedStore _scopedStore;
    private Instance? _instance;

    public event EventHandler<AlbumTrackPlayRequest>? PlayTrackRequested;
    public event EventHandler<string>? AddToQueueRequested;
    public event EventHandler<AlbumBookmarkRequest>? SaveBookmarkRequested;
    public event EventHandler<WorldDspPresetRequest>? DspPresetRegisterRequested;
    public event EventHandler? DspPresetReleaseRequested;
    public event EventHandler<string>? AchievementUnlocked;
    public event EventHandler<string>? SwitchToHtmlRequested;

    /// <param name="storeKey">World id — must match the storeKey the HTML-mode surface for the
    /// same world uses, so both runtimes share one <see cref="CapsuleScopedStore"/> file.</param>
    public WasmWorldHost(string storeKey)
    {
        using var config = new Config().WithFuelConsumption(true);
        _engine = new Engine(config);
        _store = new Store(_engine);
        _store.Fuel = FuelBudgetPerCall;
        _linker = new Linker(_engine);
        _scopedStore = new CapsuleScopedStore(storeKey);
        DefineHostImports();
    }

    public bool IsLoaded => _instance is not null;

    /// <summary>Compiles and instantiates the module and invokes its <c>on_load</c> export (if any).
    /// Returns false on any failure (malformed module, missing required imports, or a trap during
    /// on_load) — the host is left in the unloaded state, safe to retry or abandon.</summary>
    public bool Load(byte[] wasmBytes)
    {
        try
        {
            using var module = Module.FromBytes(_engine, "spectral-world", wasmBytes);
            _instance = _linker.Instantiate(_store, module);
        }
        catch (WasmtimeException)
        {
            _instance = null;
            return false;
        }

        _store.Fuel = FuelBudgetPerCall;
        try
        {
            _instance.GetAction("on_load")?.Invoke();
        }
        catch (WasmtimeException)
        {
            // A trapping on_load leaves the world half-initialized but still callable —
            // treat it as a failed load rather than pretending it's usable.
            _instance = null;
            return false;
        }

        return true;
    }

    /// <summary>Calls the module's <c>on_tick(positionSeconds: f64, playing: i32)</c> export, if any.
    /// A trap here (e.g. fuel exhaustion from a runaway frame) is swallowed — the world simply
    /// doesn't get this tick, matching the Jint runtime's non-fatal-per-frame error handling.</summary>
    public void Tick(double positionSeconds, bool playing)
    {
        if (_instance is null)
        {
            return;
        }

        _store.Fuel = FuelBudgetPerCall;
        try
        {
            _instance.GetAction<double, int>("on_tick")?.Invoke(positionSeconds, playing ? 1 : 0);
        }
        catch (WasmtimeException)
        {
            // Dropped frame — non-fatal.
        }
    }

    /// <summary>
    /// Calls a zero-argument, no-return export by name, if the module exports it — for
    /// UI-triggered actions that don't fit <see cref="Tick"/>'s per-frame shape (e.g. a "resume
    /// story" button invoking a world-defined export). A trap or missing export is swallowed,
    /// same non-fatal handling as every other guest call.
    /// </summary>
    public void TriggerExport(string exportName)
    {
        if (_instance is null)
        {
            return;
        }

        _store.Fuel = FuelBudgetPerCall;
        try
        {
            _instance.GetAction(exportName)?.Invoke();
        }
        catch (WasmtimeException)
        {
            // Dropped call — non-fatal.
        }
    }

    /// <summary>Calls <c>on_unload</c> (if present) and detaches the instance. The host itself
    /// stays reusable for <see cref="Load"/> with a different module.</summary>
    public void Unload()
    {
        if (_instance is null)
        {
            return;
        }

        _store.Fuel = FuelBudgetPerCall;
        try
        {
            _instance.GetAction("on_unload")?.Invoke();
        }
        catch (WasmtimeException)
        {
            // Non-fatal — we're tearing down anyway.
        }

        _instance = null;
    }

    private void DefineHostImports()
    {
        _linker.DefineFunction("spectral", "play_track", (Caller caller, int ptr, int len, double pos) =>
        {
            var trackId = ReadString(caller, ptr, len);
            if (trackId is null)
            {
                return;
            }

            PlayTrackRequested?.Invoke(this, new AlbumTrackPlayRequest
            {
                TrackId = trackId,
                PositionSeconds = double.IsFinite(pos) && pos >= 0 ? pos : 0,
            });
        });

        _linker.DefineFunction("spectral", "add_to_queue", (Caller caller, int ptr, int len) =>
        {
            var trackId = ReadString(caller, ptr, len);
            if (trackId is not null)
            {
                AddToQueueRequested?.Invoke(this, trackId);
            }
        });

        _linker.DefineFunction("spectral", "save_bookmark",
            (Caller caller, int trackIdPtr, int trackIdLen, double pos, int labelPtr, int labelLen) =>
            {
                var trackId = ReadString(caller, trackIdPtr, trackIdLen);
                var label = ReadString(caller, labelPtr, labelLen);
                if (trackId is null || label is null)
                {
                    return;
                }

                SaveBookmarkRequested?.Invoke(this, new AlbumBookmarkRequest
                {
                    TrackId = trackId,
                    PositionSeconds = double.IsFinite(pos) && pos >= 0 ? pos : 0,
                    Label = label.Length > 256 ? label[..256] : label,
                });
            });

        _linker.DefineFunction("spectral", "register_dsp_preset", (Caller caller, int ptr, int len) =>
        {
            var presetJson = ReadString(caller, ptr, len, maxBytes: 64 * 1024);
            if (presetJson is not null)
            {
                DspPresetRegisterRequested?.Invoke(this, new WorldDspPresetRequest { PresetChainJson = presetJson });
            }
        });

        _linker.DefineFunction("spectral", "release_dsp_preset", (Caller _) =>
        {
            DspPresetReleaseRequested?.Invoke(this, EventArgs.Empty);
        });

        _linker.DefineFunction("spectral", "unlock_achievement", (Caller caller, int ptr, int len) =>
        {
            var achievementId = ReadString(caller, ptr, len);
            if (achievementId is not null)
            {
                AchievementUnlocked?.Invoke(this, achievementId);
            }
        });

        _linker.DefineFunction("spectral", "switch_to_html", (Caller caller, int ptr, int len) =>
        {
            // Entry point may be empty (world's default HTML fallback) — that's valid.
            var entry = ReadString(caller, ptr, len) ?? string.Empty;
            SwitchToHtmlRequested?.Invoke(this, entry);
        });

        _linker.DefineFunction("spectral", "storage_get",
            (Caller caller, int keyPtr, int keyLen, int outPtr, int outCap) =>
            {
                var key = ReadString(caller, keyPtr, keyLen);
                if (key is null)
                {
                    return -1;
                }

                var value = _scopedStore.Get(key);
                if (value is null)
                {
                    return -1;
                }

                var bytes = Encoding.UTF8.GetBytes(value.ToJsonString());
                if (bytes.Length > outCap)
                {
                    return -1;
                }

                if (!caller.TryGetMemorySpan<byte>("memory", outPtr, bytes.Length, out var dest))
                {
                    return -1;
                }

                bytes.CopyTo(dest);
                return bytes.Length;
            });

        _linker.DefineFunction("spectral", "storage_set",
            (Caller caller, int keyPtr, int keyLen, int valPtr, int valLen) =>
            {
                var key = ReadString(caller, keyPtr, keyLen);
                var valueJson = ReadString(caller, valPtr, valLen, maxBytes: CapsuleScopedStore.MaxValueBytes);
                if (key is null || valueJson is null)
                {
                    return;
                }

                try
                {
                    _scopedStore.Set(key, System.Text.Json.Nodes.JsonNode.Parse(valueJson));
                }
                catch (System.Text.Json.JsonException)
                {
                    // Malformed guest input — dropped, never fatal.
                }
            });
    }

    /// <summary>Reads a UTF-8 string out of the guest's exported "memory". Returns null on any
    /// out-of-bounds/oversized/invalid request — callers treat that as "drop this call".</summary>
    private static string? ReadString(Caller caller, int ptr, int len, int maxBytes = MaxStringBytes)
    {
        if (ptr < 0 || len < 0 || len > maxBytes)
        {
            return null;
        }

        if (!caller.TryGetMemorySpan<byte>("memory", ptr, len, out var span))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(span);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _instance = null;
        _linker.Dispose();
        _store.Dispose();
        _engine.Dispose();
    }
}
