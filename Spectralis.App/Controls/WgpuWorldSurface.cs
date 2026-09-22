using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Spectralis.App.Services;
using Spectralis.App.Worlds;
using Spectralis.Core.Integrations.Web;
using Spectralis.Core.Worlds;

namespace Spectralis.App.Controls;

/// <summary>
/// Composites a sandboxed Wasm/wgpu album world into the Avalonia visual tree. Owns the
/// wgpu-host renderer, a background render thread (see the threading note below — rendering
/// must never run on the UI thread), and a <see cref="WasmWorldHost"/>. Drop it into any
/// <see cref="ContentControl"/>'s <c>Content</c> the same way <c>NowPlayingView</c> already
/// swaps in its WebView control for the HTML-mode surface — this is the Wasm-mode sibling of
/// that same slot, not a new container.
///
/// Camera: this control no longer computes a camera itself — WASD-held state and mouse-drag
/// look deltas are forwarded into the guest every frame via <see cref="WasmWorldHost.Input"/>,
/// and the frame is rendered from whatever pose the guest reports back via
/// <see cref="WasmWorldHost.GetCameraPose"/> (backed by its own <c>set_camera_pose</c> host
/// import call). A world that never calls <c>set_camera_pose</c> just sits at
/// <see cref="CameraPose.Default"/>.
///
/// Threading: <see cref="WgpuHostNative.wgpu_host_render"/> does a synchronous GPU submit +
/// blocking device.poll + buffer-map round trip every call. Running that on Avalonia's single
/// UI thread stalls pointer-input processing behind it (confirmed: felt like input landing a
/// full second late in the standalone test rig). Rendering runs on a dedicated background
/// thread here; only the finished bitmap crosses back via <see cref="Dispatcher.UIThread"/>.
/// </summary>
public sealed class WgpuWorldSurface : Image, IDisposable
{
    private WgpuWorldRenderer? _renderer;
    private WasmWorldHost? _wasmHost;

    // Written from the UI thread (pointer/keyboard events), consumed-and-reset once per frame on
    // the render thread — see RenderLoop. Guarded by _inputLock rather than volatile fields since
    // several related values (both look-delta axes, the interact edge flag) need to be read and
    // cleared together atomically.
    private readonly object _inputLock = new();
    private bool _moveForwardHeld;
    private bool _moveBackHeld;
    private bool _moveLeftHeld;
    private bool _moveRightHeld;
    private double _pendingLookYawDelta;
    private double _pendingLookPitchDelta;
    private bool _pendingInteract;
    private DateTime _lastFrameUtc;

    // A world's on_load/on_tick can call submit_geometry/submit_texture from either the render
    // thread (during Tick, under _wasmSync) or the UI thread (a TriggerExport-driven action, also
    // under _wasmSync) — but the native renderer handle itself is only ever touched from the
    // render thread (RenderFrameBgraPixels runs there, unsynchronized, since nothing else was
    // expected to call into it). Rather than add locking around every native renderer call,
    // submissions are staged here and applied on the render thread right before the next frame —
    // keeps the "one thread owns the native handle" invariant intact.
    private readonly object _geometryLock = new();
    private WorldGeometrySubmission? _pendingGeometry;
    private WorldTextureSubmission? _pendingTexture;

    // Guards every call into _wasmHost: the render thread's per-frame Tick() and any
    // UI-thread-triggered TriggerExport() must never run concurrently against the same
    // wasmtime Store (see WasmWorldTestWindow, where this pattern was first proven out).
    private readonly object _wasmSync = new();

    private Thread? _renderThread;
    private volatile bool _running;
    private DateTime _startedUtc;
    private long _frameCount;

    private bool _dragging;
    private Point _lastPointer;

    // UI-thread-only — every read/write happens from a control event handler or a
    // Dispatcher.UIThread.Post callback, never the render thread directly.
    private bool _pointerLocked;
    private Cursor? _cursorBeforeLock;

    private WriteableBitmap? _displayBitmap;

    public bool IsWasmAvailable => WgpuWorldRenderer.IsAvailable;
    public bool IsAttached => _renderer is not null;
    public bool IsPointerLocked => _pointerLocked;

    public WgpuWorldSurface()
    {
        // Needed to actually receive OnKeyDown/OnKeyUp for WASD — Avalonia only routes key
        // events to a focused element, and an Image isn't focusable by default.
        Focusable = true;

        // AttachWorld's own Focus() call can lose the race against layout if this control was
        // just assigned as Content and hasn't reached the visual tree yet — re-attempt here,
        // which fires once it actually has, so WASD works without needing a click first even
        // when that race is lost.
        AttachedToVisualTree += (_, _) =>
        {
            Focus();
            WasmWorldLog.Log($"OnAttachedToVisualTree: Focus() retried, IsFocused={IsFocused}");
        };
    }

    public event EventHandler<AlbumTrackPlayRequest>? PlayTrackRequested;
    public event EventHandler<string>? AddToQueueRequested;
    public event EventHandler<AlbumBookmarkRequest>? SaveBookmarkRequested;
    public event EventHandler<WorldDspPresetRequest>? DspPresetRegisterRequested;
    public event EventHandler? DspPresetReleaseRequested;
    public event EventHandler<string>? AchievementUnlocked;
    public event EventHandler<string>? SwitchToHtmlRequested;

    /// <summary>
    /// Loads and starts rendering <paramref name="wasmBytes"/> under <paramref name="storeKey"/>
    /// (the world id — must match whatever storeKey the HTML-mode surface for the same world
    /// uses, so hand-off state round-trips a runtime switch via the shared
    /// <see cref="Spectralis.Core.Capsule.CapsuleScopedStore"/>). <paramref name="allowPointerLock"/>
    /// mirrors the manifest's <c>worlds.pointerLock</c> capability — see
    /// <see cref="WasmWorldHost(string, bool)"/>. Returns false if the native renderer is
    /// unavailable (no GPU/library) or the module fails to load — caller should fall back to the
    /// HTML surface in that case, same as a WebView navigation failure today.
    /// </summary>
    public bool AttachWorld(byte[] wasmBytes, string storeKey, int width = 960, int height = 720, bool allowPointerLock = false)
    {
        DetachWorld();
        WasmWorldLog.Log($"AttachWorld: storeKey={storeKey} bytes={wasmBytes.Length} size={width}x{height} allowPointerLock={allowPointerLock}");

        _renderer = WgpuWorldRenderer.Create(width, height);
        if (_renderer is null)
        {
            WasmWorldLog.Log("AttachWorld: WgpuWorldRenderer.Create returned null — see the line above for why");
            return false;
        }

        _wasmHost = new WasmWorldHost(storeKey, allowPointerLock);
        _wasmHost.PlayTrackRequested += (_, e) => PlayTrackRequested?.Invoke(this, e);
        _wasmHost.AddToQueueRequested += (_, e) => AddToQueueRequested?.Invoke(this, e);
        _wasmHost.SaveBookmarkRequested += (_, e) => SaveBookmarkRequested?.Invoke(this, e);
        _wasmHost.DspPresetRegisterRequested += (_, e) => DspPresetRegisterRequested?.Invoke(this, e);
        _wasmHost.DspPresetReleaseRequested += (_, e) => DspPresetReleaseRequested?.Invoke(this, e);
        _wasmHost.AchievementUnlocked += (_, e) => AchievementUnlocked?.Invoke(this, e);
        _wasmHost.SwitchToHtmlRequested += (_, e) => SwitchToHtmlRequested?.Invoke(this, e);
        // Requested/released from either the UI thread (on_load, during the Load() call just
        // below) or the render thread (on_input/on_tick, under _wasmSync) — always hop to the UI
        // thread since engaging/disengaging touches the Cursor property and native cursor calls.
        _wasmHost.PointerLockRequested += (_, _) => Dispatcher.UIThread.Post(EngagePointerLock);
        _wasmHost.PointerLockReleased += (_, _) => Dispatcher.UIThread.Post(DisengagePointerLock);
        _wasmHost.GeometrySubmitted += (_, e) =>
        {
            lock (_geometryLock)
            {
                _pendingGeometry = e;
            }
        };
        _wasmHost.TextureSubmitted += (_, e) =>
        {
            lock (_geometryLock)
            {
                _pendingTexture = e;
            }
        };

        bool loaded;
        lock (_wasmSync)
        {
            loaded = _wasmHost.Load(wasmBytes);
        }

        if (!loaded)
        {
            WasmWorldLog.Log("AttachWorld: WasmWorldHost.Load returned false (bad module or trapping on_load)");
            DetachWorld();
            return false;
        }

        _startedUtc = DateTime.UtcNow;
        _lastFrameUtc = default;
        lock (_inputLock)
        {
            _moveForwardHeld = _moveBackHeld = _moveLeftHeld = _moveRightHeld = false;
            _pendingLookYawDelta = _pendingLookPitchDelta = 0;
            _pendingInteract = false;
        }
        _frameCount = 0;
        _running = true;
        _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "WgpuWorldSurface-Render" };
        _renderThread.Start();

        // Without this, WASD/mouselook silently do nothing until the listener happens to click
        // the surface first — Avalonia only routes key events to whatever's focused, and nothing
        // focuses this control just by attaching a world to it.
        Focus();
        WasmWorldLog.Log($"AttachWorld: loaded ok, render thread started, Focus() called, IsFocused={IsFocused}");
        return true;
    }

    /// <summary>Calls a zero-argument export on the active world (see <see cref="WasmWorldHost.TriggerExport"/>).</summary>
    public void TriggerExport(string exportName)
    {
        lock (_wasmSync)
        {
            _wasmHost?.TriggerExport(exportName);
        }
    }

    public void DetachWorld()
    {
        if (_renderThread is not null)
        {
            WasmWorldLog.Log($"DetachWorld: stopping render thread after {_frameCount} frames");
        }

        _running = false;
        _renderThread?.Join(TimeSpan.FromSeconds(2));
        _renderThread = null;

        // Leaving pointer lock engaged past DetachWorld would strand the listener with a hidden,
        // recentering cursor and no world left to release it — no guest to notify at this point,
        // just restore the cursor.
        if (_pointerLocked)
        {
            _pointerLocked = false;
            Cursor = _cursorBeforeLock;
            _cursorBeforeLock = null;
        }

        lock (_wasmSync)
        {
            _wasmHost?.Dispose();
            _wasmHost = null;
        }

        _renderer?.Dispose();
        _renderer = null;
        Source = null;

        lock (_geometryLock)
        {
            _pendingGeometry = null;
            _pendingTexture = null;
        }
    }

    /// <summary>Hides the cursor and switches <see cref="OnPointerMoved"/> to always-on look
    /// (no button hold needed), warping the cursor back to this control's center after every
    /// move so it never runs out of screen to move across — the standard "pointer lock without a
    /// native OS API for it" trick. UI-thread only (touches <c>Cursor</c>).</summary>
    private void EngagePointerLock()
    {
        if (_pointerLocked || _wasmHost is null)
        {
            return;
        }

        _pointerLocked = true;
        _cursorBeforeLock = Cursor;
        Cursor = new Cursor(StandardCursorType.None);
        RecenterCursor();

        lock (_wasmSync)
        {
            _wasmHost?.NotifyPointerLockChanged(true);
        }

        WasmWorldLog.Log("EngagePointerLock: locked=true");
    }

    /// <summary>Restores the cursor and drops back to click-drag-to-look. Safe to call whether
    /// or not a lock is currently active (a world releasing a lock it doesn't hold, or the panic
    /// escape firing with no lock engaged, are both just no-ops here). UI-thread only.</summary>
    private void DisengagePointerLock()
    {
        if (!_pointerLocked)
        {
            return;
        }

        _pointerLocked = false;
        Cursor = _cursorBeforeLock;
        _cursorBeforeLock = null;

        lock (_wasmSync)
        {
            _wasmHost?.NotifyPointerLockChanged(false);
        }

        WasmWorldLog.Log("DisengagePointerLock: locked=false");
    }

    /// <summary>The hold-Esc panic escape (see <c>MainWindow.OnWindowKeyDown</c>): force-releases
    /// pointer lock regardless of whether the active world ever calls <c>release_pointer_lock</c>
    /// itself. Identical to a normal release from the guest's point of view — it still gets
    /// <c>on_pointer_lock_change(0)</c> — the only difference is who decided.</summary>
    public void PanicReleasePointerLock() => DisengagePointerLock();

    /// <summary>Warps the OS cursor to this control's center in screen coordinates (Windows
    /// only — see <see cref="CursorNative"/>) and resets <see cref="_lastPointer"/> to match, so
    /// the synthetic PointerMoved the warp itself generates computes a ~zero delta instead of a
    /// spurious jump.</summary>
    private void RecenterCursor()
    {
        var localCenter = new Point(Bounds.Width / 2, Bounds.Height / 2);
        _lastPointer = localCenter;

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var topLevelPoint = this.TranslatePoint(localCenter, topLevel) ?? localCenter;
        var screenPoint = topLevel.PointToScreen(topLevelPoint);
        CursorNative.RecenterTo(screenPoint.X, screenPoint.Y);
    }

    private void RenderLoop()
    {
        while (_running)
        {
            var renderer = _renderer;
            if (renderer is null)
            {
                return;
            }

            WorldGeometrySubmission? geometry;
            WorldTextureSubmission? texture;
            lock (_geometryLock)
            {
                geometry = _pendingGeometry;
                _pendingGeometry = null;
                texture = _pendingTexture;
                _pendingTexture = null;
            }

            // Texture first — geometry submitted the same frame may carry uv coordinates meant
            // to address whatever atlas is bound by the time it renders.
            if (texture is not null)
            {
                renderer.SetTexture(texture.Rgba, texture.Width, texture.Height);
            }

            if (geometry is not null)
            {
                renderer.SubmitGeometry(geometry.InterleavedVertices, geometry.Indices);
            }

            var now = DateTime.UtcNow;
            var dt = _lastFrameUtc == default ? 0.0 : (now - _lastFrameUtc).TotalSeconds;
            _lastFrameUtc = now;

            bool moveForwardHeld, moveBackHeld, moveLeftHeld, moveRightHeld;
            double moveForward, moveRight, lookYawDelta, lookPitchDelta;
            bool interact;
            lock (_inputLock)
            {
                moveForwardHeld = _moveForwardHeld;
                moveBackHeld = _moveBackHeld;
                moveLeftHeld = _moveLeftHeld;
                moveRightHeld = _moveRightHeld;
                moveForward = (_moveForwardHeld ? 1.0 : 0.0) - (_moveBackHeld ? 1.0 : 0.0);
                moveRight = (_moveRightHeld ? 1.0 : 0.0) - (_moveLeftHeld ? 1.0 : 0.0);
                lookYawDelta = _pendingLookYawDelta;
                lookPitchDelta = _pendingLookPitchDelta;
                interact = _pendingInteract;
                _pendingLookYawDelta = 0;
                _pendingLookPitchDelta = 0;
                _pendingInteract = false;
            }
            moveForward *= dt;
            moveRight *= dt;

            var t = (now - _startedUtc).TotalSeconds;
            lock (_wasmSync)
            {
                _wasmHost?.Input(moveForward, moveRight, lookYawDelta, lookPitchDelta, interact);
                _wasmHost?.Tick(t, playing: true);
            }

            var pose = _wasmHost?.GetCameraPose() ?? CameraPose.Default;
            var pixels = renderer.RenderFrameBgraPixels(
                t, (float)pose.X, (float)pose.Y, (float)pose.Z, (float)pose.Yaw, (float)pose.Pitch);
            if (pixels is not null)
            {
                var snapshot = (byte[])pixels.Clone();
                Dispatcher.UIThread.Post(() => ApplyFrame(snapshot, renderer.Width, renderer.Height));
            }

            _frameCount++;
            // First few frames unconditionally (startup diagnostics), then roughly once a
            // second's worth of frames after that — enough to see whether held-key/look-delta
            // input is actually arriving here and whether the pose is actually changing, without
            // writing a line per frame for the life of the session.
            if (_frameCount <= 5 || _frameCount % 60 == 0)
            {
                // IsFocused is UI-thread-affine — don't touch it from this background thread.
                WasmWorldLog.Log(
                    $"frame #{_frameCount}: keys[W={moveForwardHeld} A={moveLeftHeld} S={moveBackHeld} D={moveRightHeld}] " +
                    $"lookDelta=({lookYawDelta:F4},{lookPitchDelta:F4}) interact={interact} " +
                    $"pose=({pose.X:F2},{pose.Y:F2},{pose.Z:F2} yaw={pose.Yaw:F2} pitch={pose.Pitch:F2}) " +
                    $"frameRendered={pixels is not null}");
            }
        }
    }

    private void ApplyFrame(byte[] bgraPixels, int width, int height)
    {
        if (!_running)
        {
            return;
        }

        if (_displayBitmap is null || _displayBitmap.PixelSize.Width != width || _displayBitmap.PixelSize.Height != height)
        {
            _displayBitmap?.Dispose();
            _displayBitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        }

        using (var buffer = _displayBitmap.Lock())
        {
            System.Runtime.InteropServices.Marshal.Copy(bgraPixels, 0, buffer.Address, bgraPixels.Length);
        }

        // Same WriteableBitmap instance every frame (only recreated on a size change) — Avalonia's
        // Source setter no-ops on a reference that hasn't changed, so it has no way to know the
        // pixels inside it did. Without this, the image only actually redraws when something else
        // (an unrelated dialog, a window resize) happens to force a repaint — confirmed: real
        // mouse-look moved the reported camera pose frame over frame but the screen sat frozen on
        // whatever the last incidental repaint had caught.
        Source = _displayBitmap;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        _dragging = true;
        _lastPointer = e.GetPosition(this);
        // Without this, a fast look-around drag that swings the cursor past this control's
        // edge (easy to do — the surface doesn't fill the whole window) stops delivering
        // PointerMoved the instant the cursor leaves its bounds, so look would stall out mid-turn
        // instead of tracking to the edge of the screen and beyond.
        e.Pointer.Capture(this);
        WasmWorldLog.Log($"OnPointerPressed: dragging=true captured={ReferenceEquals(e.Pointer.Captured, this)} IsFocused={IsFocused}");
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging && !_pointerLocked)
        {
            return;
        }

        var pos = e.GetPosition(this);
        var delta = pos - _lastPointer;
        _lastPointer = pos;
        lock (_inputLock)
        {
            // Raw radians-per-pixel deltas since the last frame — the guest owns yaw/pitch
            // accumulation (and pitch clamping) from here via on_input, this control no longer
            // tracks a camera angle of its own.
            _pendingLookYawDelta += delta.X * 0.01;
            _pendingLookPitchDelta += -delta.Y * 0.01;
        }

        // Pointer-locked mode ignores _dragging entirely (look works without holding a button)
        // and re-centers after every move instead of waiting for the cursor to reach an edge —
        // with the cursor hidden, its real position is invisible anyway, so there's nothing lost
        // by never letting it wander far from center.
        if (_pointerLocked)
        {
            RecenterCursor();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        if (ReferenceEquals(e.Pointer.Captured, this))
        {
            e.Pointer.Capture(null);
        }
        WasmWorldLog.Log("OnPointerReleased: dragging=false");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        SetMoveKeyHeld(e.Key, held: true);
        if (e.Key is Key.E or Key.Space)
        {
            lock (_inputLock)
            {
                _pendingInteract = true;
            }
        }

        if (e.Key is Key.W or Key.A or Key.S or Key.D or Key.E or Key.Space)
        {
            WasmWorldLog.Log($"OnKeyDown: {e.Key}");
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        SetMoveKeyHeld(e.Key, held: false);
    }

    private void SetMoveKeyHeld(Key key, bool held)
    {
        lock (_inputLock)
        {
            switch (key)
            {
                case Key.W:
                    _moveForwardHeld = held;
                    break;
                case Key.S:
                    _moveBackHeld = held;
                    break;
                case Key.A:
                    _moveLeftHeld = held;
                    break;
                case Key.D:
                    _moveRightHeld = held;
                    break;
            }
        }
    }

    public void Dispose() => DetachWorld();
}
