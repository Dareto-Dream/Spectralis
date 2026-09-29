using System.Text;
using Spectralis.Core.Capsule;
using Wasmtime;
using AlbumTrackPlayRequest = Spectralis.Core.Integrations.Web.AlbumTrackPlayRequest;
using AlbumBookmarkRequest = Spectralis.Core.Integrations.Web.AlbumBookmarkRequest;
using WorldDspPresetRequest = Spectralis.Core.Integrations.Web.WorldDspPresetRequest;

namespace Spectralis.Core.Worlds;

/// <summary>One <c>submit_geometry</c> call's payload — see <see cref="WasmWorldHost.GeometrySubmitted"/>.
/// Vertex layout is <c>[f32;3] position, [f32;2] uv, [f32;3] color</c> interleaved, matching
/// wgpu-host's fixed <c>Vertex</c> struct. <c>uv</c> addresses whatever atlas was last submitted
/// via <c>submit_texture</c> (see <see cref="WorldTextureSubmission"/>) — irrelevant until a
/// world actually submits one, since the default atlas is a 1x1 white pixel.</summary>
public sealed class WorldGeometrySubmission
{
    public required float[] InterleavedVertices { get; init; }
    public required uint[] Indices { get; init; }
}

/// <summary>One <c>submit_texture</c> call's payload — see <see cref="WasmWorldHost.TextureSubmitted"/>.
/// <c>Rgba</c> is tightly-packed RGBA8, row-major, exactly <c>Width * Height * 4</c> bytes.</summary>
public sealed class WorldTextureSubmission
{
    public required byte[] Rgba { get; init; }
    public required uint Width { get; init; }
    public required uint Height { get; init; }
}

/// <summary>A world's positional first-person camera, as last reported via <c>set_camera_pose</c>
/// — world-space eye position plus yaw/pitch (radians) look direction. There's no orbit target
/// and no fixed distance; the guest walks this around on its own (e.g. accumulating movement in
/// <see cref="WasmWorldHost.Input"/>) and pushes the result here every frame.</summary>
/// <summary>One <c>set_interact_target</c> call's payload — see <see cref="WasmWorldHost.InteractTargetChanged"/>.
/// Null <see cref="Id"/> means the guest cleared its target (nothing in reach/reticle).</summary>
public sealed record InteractTarget(string? Id, string? Prompt);

/// <summary>One <c>start_story</c> call's payload — see <see cref="WasmWorldHost.StoryRequested"/>.</summary>
public sealed record StoryRequest(string TrackId, IReadOnlyList<string> Pages);

public readonly record struct CameraPose(double X, double Y, double Z, double Yaw, double Pitch)
{
    /// <summary>Eye a few units back on +Z looking toward the origin (yaw = pi), matching where
    /// the old orbit camera's default view sat — used until a world calls <c>set_camera_pose</c>
    /// for the first time.</summary>
    public static readonly CameraPose Default = new(0, 1.6, 4.0, Math.PI, 0);
}

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
///   submit_geometry(vertsPtr,vertsByteLen,idxPtr,idxByteLen) -> i32 (1 = accepted, 0 = rejected)
///                                              — replaces the rendered scene's geometry; see
///                                                WorldGeometrySubmission for the vertex layout.
///                                                Indices are u32 (idxByteLen a multiple of 4).
///   submit_texture(rgbaPtr,rgbaByteLen,width,height: i32) -> i32 (1 = accepted, 0 = rejected)
///                                              — replaces the shared texture atlas guest
///                                                geometry's uv attribute samples against; see
///                                                WorldTextureSubmission.
///   set_camera_pose(x,y,z,yaw,pitch: f64)     — reports the guest's own positional camera; see
///                                                CameraPose. wgpu-host has no orbit/target math
///                                                anymore, so a world that never calls this stays
///                                                parked at CameraPose.Default.
///   request_pointer_lock() -> i32 (1 = granted, 0 = denied) — asks the host to hide/recenter the
///                                                cursor and deliver continuous look deltas without
///                                                needing a button held. Denied (returns 0, no
///                                                PointerLockRequested event) unless this world's
///                                                manifest declared worlds.pointerLock — same
///                                                silently-dropped-without-the-capability shape as
///                                                register_dsp_preset, not a trap.
///   release_pointer_lock()                    — gives the lock back voluntarily (e.g. the world's
///                                                about to show its own cursor-driven UI). Always
///                                                allowed — releasing something you were never
///                                                granted is a no-op, not a capability violation.
///   set_interact_target(idPtr,idLen,promptPtr,promptLen) — reports what the guest's own reticle
///                                                hit-test is currently looking at (0-length id =
///                                                clear/nothing); see InteractTargetChanged. The
///                                                host renders the reticle/prompt, the guest owns
///                                                deciding what's interactable and from how far.
///   start_story(trackIdPtr,trackIdLen,pagesPtr,pagesLen) — hands a bottom-third VN pager over to
///                                                the host; pagesPtr is UTF-8 pages joined by a
///                                                single NUL byte (no JSON — see chaser_room's own
///                                                doc comment on why). The host owns pagination
///                                                entirely; once the listener pages through to the
///                                                end it starts trackId directly, the same call a
///                                                guest-initiated play_track would have made. A
///                                                world that calls this should stop reacting to
///                                                on_input afterward — see StoryRequested.
///   show_achievements()                       — opens the host's achievements board (no
///                                                payload; the host already owns the session data
///                                                it needs). Unlike start_story this is
///                                                reversible — see AchievementsRequested and the
///                                                on_achievements_closed export below.
///
/// Guest export called from the host (beyond on_load/on_tick/on_unload):
///   on_pointer_lock_change(locked: i32) — optional; fires whenever the *actual* lock state
///     changes, whichever side caused it — a granted request_pointer_lock, this world's own
///     release_pointer_lock, or the host unilaterally taking it back (the listener's hold-Esc
///     panic escape does exactly that). A world that only tracked its own requests would never
///     notice the last case.
///   on_track_completed(playedSeconds,durationSeconds: f64) — optional; the Wasm-side counterpart
///     of the HTML surface's onTrackCompleted, fired when a track this world started via
///     play_track reaches its natural end. The listener is switched back to this world (not left
///     on whatever surface the track itself used) right before this fires.
///   on_input(moveForward,moveRight,lookYawDelta,lookPitchDelta: f64, interact: i32) — optional;
///     called once per rendered frame, ahead of on_tick, so a world can update its own position
///     via set_camera_pose before that frame renders. moveForward/moveRight are already
///     dt-scaled unit axis values (host multiplies held-key state by elapsed seconds) — a world
///     picks its own movement speed and multiplies. lookYawDelta/lookPitchDelta are raw pointer-
///     drag deltas in radians since the last frame, un-clamped (a world should clamp its own
///     accumulated pitch to avoid flipping over). interact is 1 on the frame an interact
///     key/click was pressed, 0 otherwise (edge-triggered, not held).
///   on_achievements_closed() — optional; called via <see cref="TriggerExport"/> (the same
///     generic UI-triggered-action mechanism every other zero-arg export uses) once the listener
///     dismisses the achievements board a show_achievements call opened. A world that never calls
///     show_achievements has no reason to export this.
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

    // Mirrors wgpu-host's MAX_VERTICES (500000) / MAX_INDICES (1500000) caps — reject oversized
    // submissions here, before ever touching the native renderer, rather than relying solely on
    // its own check. Sized with headroom over an actual measured room export (indie_bedroom.blend,
    // full detail: ~211k vertices / ~1.2M indices).
    private const int BytesPerVertex = 32; // [f32;3] position + [f32;2] uv + [f32;3] color
    private const int MaxVertexBytes = 500_000 * BytesPerVertex;
    private const int MaxIndexBytes = 1_500_000 * sizeof(uint);

    // Mirrors wgpu-host's MAX_TEXTURE_DIM (4096) cap.
    private const int MaxTextureDim = 4096;
    private const int MaxTextureBytes = MaxTextureDim * MaxTextureDim * 4;

    private readonly Engine _engine;
    private readonly Store _store;
    private readonly Linker _linker;
    private readonly CapsuleScopedStore _scopedStore;
    private Instance? _instance;

    private readonly object _poseLock = new();
    private CameraPose _cameraPose = CameraPose.Default;

    public event EventHandler<AlbumTrackPlayRequest>? PlayTrackRequested;
    public event EventHandler<string>? AddToQueueRequested;
    public event EventHandler<AlbumBookmarkRequest>? SaveBookmarkRequested;
    public event EventHandler<WorldDspPresetRequest>? DspPresetRegisterRequested;
    public event EventHandler? DspPresetReleaseRequested;
    public event EventHandler<string>? AchievementUnlocked;
    public event EventHandler<string>? SwitchToHtmlRequested;
    public event EventHandler<WorldGeometrySubmission>? GeometrySubmitted;
    public event EventHandler<WorldTextureSubmission>? TextureSubmitted;
    public event EventHandler? PointerLockRequested;
    public event EventHandler? PointerLockReleased;
    public event EventHandler<InteractTarget>? InteractTargetChanged;
    public event EventHandler<StoryRequest>? StoryRequested;
    public event EventHandler? AchievementsRequested;

    private readonly bool _allowPointerLock;

    /// <summary>Whether this world's manifest declared <c>worlds.pointerLock</c> — lets the host
    /// surface gate a direct re-engage (e.g. the pause menu's Resume button) the same way it gates
    /// the guest's own <c>request_pointer_lock</c> import call.</summary>
    public bool AllowPointerLock => _allowPointerLock;

    /// <param name="storeKey">World id — must match the storeKey the HTML-mode surface for the
    /// same world uses, so both runtimes share one <see cref="CapsuleScopedStore"/> file.</param>
    /// <param name="allowPointerLock">Whether this world's manifest declared the
    /// <c>worlds.pointerLock</c> capability — gates <c>request_pointer_lock</c> the same way a
    /// missing <c>audio.dspPreset</c> silently drops <c>register_dsp_preset</c>. Release is
    /// always allowed regardless (giving up a lock you were never granted is a no-op, not a
    /// violation).</param>
    public WasmWorldHost(string storeKey, bool allowPointerLock = false)
    {
        using var config = new Config().WithFuelConsumption(true);
        _engine = new Engine(config);
        _store = new Store(_engine);
        _store.Fuel = FuelBudgetPerCall;
        _linker = new Linker(_engine);
        _scopedStore = new CapsuleScopedStore(storeKey);
        _allowPointerLock = allowPointerLock;
        DefineHostImports();
    }

    public bool IsLoaded => _instance is not null;

    /// <summary>The world's last-reported camera pose (see <see cref="CameraPose"/>) —
    /// <see cref="CameraPose.Default"/> until the guest calls <c>set_camera_pose</c> at least
    /// once. Safe to call from any thread; the renderer thread reads this every frame while
    /// <see cref="Input"/>/<see cref="Tick"/> (which may write it via the guest's host import
    /// calls) run on whichever thread owns the wasmtime store.</summary>
    public CameraPose GetCameraPose()
    {
        lock (_poseLock)
        {
            return _cameraPose;
        }
    }

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

    /// <summary>Calls the module's optional <c>on_input(moveForward, moveRight, lookYawDelta,
    /// lookPitchDelta: f64, interact: i32)</c> export, if any — see the class doc for the exact
    /// shape of each argument. Meant to be called once per rendered frame, ahead of
    /// <see cref="Tick"/>, so a world can react to input (typically by calling
    /// <c>set_camera_pose</c>) before that frame renders. A trap or missing export is swallowed,
    /// same non-fatal handling as every other guest call.</summary>
    public void Input(double moveForward, double moveRight, double lookYawDelta, double lookPitchDelta, bool interact)
    {
        if (_instance is null)
        {
            return;
        }

        _store.Fuel = FuelBudgetPerCall;
        try
        {
            _instance.GetAction<double, double, double, double, int>("on_input")?
                .Invoke(moveForward, moveRight, lookYawDelta, lookPitchDelta, interact ? 1 : 0);
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

    /// <summary>Calls the module's optional <c>on_pointer_lock_change(locked: i32)</c> export, if
    /// any — tells the guest what the <em>actual</em> lock state is after every change, not just
    /// after a request it made itself. The host is the real source of truth: a lock can be lost
    /// without the guest calling <c>release_pointer_lock</c> at all (the panic escape hatch does
    /// exactly that), and a world that only tracked its own requests would have no way to notice.
    /// A trap or missing export is swallowed, same non-fatal handling as every other guest call.</summary>
    public void NotifyPointerLockChanged(bool locked)
    {
        if (_instance is null)
        {
            return;
        }

        _store.Fuel = FuelBudgetPerCall;
        try
        {
            _instance.GetAction<int>("on_pointer_lock_change")?.Invoke(locked ? 1 : 0);
        }
        catch (WasmtimeException)
        {
            // Dropped call — non-fatal.
        }
    }

    /// <summary>Calls the module's optional <c>on_track_completed(played_seconds, duration_seconds: f64)</c>
    /// export, if any — the Wasm-side counterpart of the HTML surface's <c>onTrackCompleted</c>
    /// callback, fired when a track this world started (via <c>play_track</c>) reaches its
    /// natural end. No track id parameter: unlike a host-initiated call, this one only ever
    /// follows a <c>play_track</c> the guest made itself, so it already knows which track just
    /// finished — passing percentages/raw seconds back out is what it can't derive on its own.
    /// A trap or missing export is swallowed, same non-fatal handling as every other guest call.</summary>
    public void NotifyTrackCompleted(double playedSeconds, double durationSeconds)
    {
        if (_instance is null)
        {
            return;
        }

        _store.Fuel = FuelBudgetPerCall;
        try
        {
            _instance.GetAction<double, double>("on_track_completed")?.Invoke(playedSeconds, durationSeconds);
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

        _linker.DefineFunction("spectral", "submit_geometry",
            (Caller caller, int vertsPtr, int vertsByteLen, int idxPtr, int idxByteLen) =>
            {
                if (vertsByteLen <= 0 || vertsByteLen > MaxVertexBytes || vertsByteLen % BytesPerVertex != 0)
                {
                    return 0;
                }

                if (idxByteLen <= 0 || idxByteLen > MaxIndexBytes || idxByteLen % sizeof(uint) != 0)
                {
                    return 0;
                }

                if (!caller.TryGetMemorySpan<byte>("memory", vertsPtr, vertsByteLen, out var vertBytes) ||
                    !caller.TryGetMemorySpan<byte>("memory", idxPtr, idxByteLen, out var idxBytes))
                {
                    return 0;
                }

                // Wasm linear memory is always little-endian, matching every real CPU this app
                // runs on (x64/arm64) — BitConverter reads native-endian, so no swap is needed.
                var vertices = new float[vertsByteLen / sizeof(float)];
                for (var i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = BitConverter.ToSingle(vertBytes.Slice(i * sizeof(float), sizeof(float)));
                }

                var indices = new uint[idxByteLen / sizeof(uint)];
                for (var i = 0; i < indices.Length; i++)
                {
                    indices[i] = BitConverter.ToUInt32(idxBytes.Slice(i * sizeof(uint), sizeof(uint)));
                }

                GeometrySubmitted?.Invoke(this, new WorldGeometrySubmission
                {
                    InterleavedVertices = vertices,
                    Indices = indices,
                });
                return 1;
            });

        _linker.DefineFunction("spectral", "submit_texture",
            (Caller caller, int rgbaPtr, int rgbaByteLen, int width, int height) =>
            {
                if (width <= 0 || height <= 0 || width > MaxTextureDim || height > MaxTextureDim)
                {
                    return 0;
                }

                if (rgbaByteLen != width * height * 4 || rgbaByteLen > MaxTextureBytes)
                {
                    return 0;
                }

                if (!caller.TryGetMemorySpan<byte>("memory", rgbaPtr, rgbaByteLen, out var rgbaBytes))
                {
                    return 0;
                }

                TextureSubmitted?.Invoke(this, new WorldTextureSubmission
                {
                    Rgba = rgbaBytes.ToArray(),
                    Width = (uint)width,
                    Height = (uint)height,
                });
                return 1;
            });

        _linker.DefineFunction("spectral", "set_camera_pose",
            (Caller _, double x, double y, double z, double yaw, double pitch) =>
            {
                if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) ||
                    !double.IsFinite(yaw) || !double.IsFinite(pitch))
                {
                    return;
                }

                lock (_poseLock)
                {
                    _cameraPose = new CameraPose(x, y, z, yaw, pitch);
                }
            });

        _linker.DefineFunction("spectral", "request_pointer_lock", (Caller _) =>
        {
            if (!_allowPointerLock)
            {
                return 0;
            }

            PointerLockRequested?.Invoke(this, EventArgs.Empty);
            return 1;
        });

        _linker.DefineFunction("spectral", "release_pointer_lock", (Caller _) =>
        {
            PointerLockReleased?.Invoke(this, EventArgs.Empty);
        });

        _linker.DefineFunction("spectral", "set_interact_target",
            (Caller caller, int idPtr, int idLen, int promptPtr, int promptLen) =>
            {
                var id = idLen > 0 ? ReadString(caller, idPtr, idLen) : null;
                var prompt = promptLen > 0 ? ReadString(caller, promptPtr, promptLen) : null;
                InteractTargetChanged?.Invoke(this, new InteractTarget(id, prompt));
            });

        _linker.DefineFunction("spectral", "start_story",
            (Caller caller, int trackIdPtr, int trackIdLen, int pagesPtr, int pagesLen) =>
            {
                var trackId = ReadString(caller, trackIdPtr, trackIdLen);
                var pagesRaw = ReadString(caller, pagesPtr, pagesLen);
                if (trackId is null || pagesRaw is null)
                {
                    return;
                }

                var pages = pagesRaw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
                if (pages.Length == 0)
                {
                    return;
                }

                StoryRequested?.Invoke(this, new StoryRequest(trackId, pages));
            });

        _linker.DefineFunction("spectral", "show_achievements", (Caller _) =>
        {
            AchievementsRequested?.Invoke(this, EventArgs.Empty);
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
