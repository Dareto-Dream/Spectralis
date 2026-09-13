using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Spectralis.App.Worlds;
using Spectralis.Core.Integrations.Web;
using Spectralis.Core.Worlds;

namespace Spectralis.App.Controls;

/// <summary>
/// Composites a sandboxed Wasm/wgpu album world into the Avalonia visual tree. Owns the
/// wgpu-host renderer, a background render thread (see the threading note below — rendering
/// must never run on the UI thread), a <see cref="WasmWorldHost"/>, and a mouse-driven orbit
/// camera. Drop it into any <see cref="ContentControl"/>'s <c>Content</c> the same way
/// <c>NowPlayingView</c> already swaps in its WebView control for the HTML-mode surface — this
/// is the Wasm-mode sibling of that same slot, not a new container.
///
/// Threading: <see cref="WgpuHostNative.wgpu_host_render"/> does a synchronous GPU submit +
/// blocking device.poll + buffer-map round trip every call. Running that on Avalonia's single
/// UI thread stalls pointer-input processing behind it (confirmed: felt like input landing a
/// full second late in the standalone test rig). Rendering runs on a dedicated background
/// thread here; only the finished bitmap crosses back via <see cref="Dispatcher.UIThread"/>.
/// </summary>
public sealed class WgpuWorldSurface : Image, IDisposable
{
    private const float CamDist = 3.2f;

    private WgpuWorldRenderer? _renderer;
    private WasmWorldHost? _wasmHost;

    // Guards every call into _wasmHost: the render thread's per-frame Tick() and any
    // UI-thread-triggered TriggerExport() must never run concurrently against the same
    // wasmtime Store (see WasmWorldTestWindow, where this pattern was first proven out).
    private readonly object _wasmSync = new();

    private Thread? _renderThread;
    private volatile bool _running;
    private DateTime _startedUtc;

    private volatile float _camYaw;
    private volatile float _camPitch = 0.3f;

    private bool _dragging;
    private Point _lastPointer;

    private WriteableBitmap? _displayBitmap;

    public bool IsWasmAvailable => WgpuWorldRenderer.IsAvailable;
    public bool IsAttached => _renderer is not null;

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
    /// <see cref="Spectralis.Core.Capsule.CapsuleScopedStore"/>). Returns false if the native
    /// renderer is unavailable (no GPU/library) or the module fails to load — caller should
    /// fall back to the HTML surface in that case, same as a WebView navigation failure today.
    /// </summary>
    public bool AttachWorld(byte[] wasmBytes, string storeKey, int width = 960, int height = 720)
    {
        DetachWorld();

        _renderer = WgpuWorldRenderer.Create(width, height);
        if (_renderer is null)
        {
            return false;
        }

        _wasmHost = new WasmWorldHost(storeKey);
        _wasmHost.PlayTrackRequested += (_, e) => PlayTrackRequested?.Invoke(this, e);
        _wasmHost.AddToQueueRequested += (_, e) => AddToQueueRequested?.Invoke(this, e);
        _wasmHost.SaveBookmarkRequested += (_, e) => SaveBookmarkRequested?.Invoke(this, e);
        _wasmHost.DspPresetRegisterRequested += (_, e) => DspPresetRegisterRequested?.Invoke(this, e);
        _wasmHost.DspPresetReleaseRequested += (_, e) => DspPresetReleaseRequested?.Invoke(this, e);
        _wasmHost.AchievementUnlocked += (_, e) => AchievementUnlocked?.Invoke(this, e);
        _wasmHost.SwitchToHtmlRequested += (_, e) => SwitchToHtmlRequested?.Invoke(this, e);

        bool loaded;
        lock (_wasmSync)
        {
            loaded = _wasmHost.Load(wasmBytes);
        }

        if (!loaded)
        {
            DetachWorld();
            return false;
        }

        _startedUtc = DateTime.UtcNow;
        _camYaw = 0f;
        _camPitch = 0.3f;
        _running = true;
        _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "WgpuWorldSurface-Render" };
        _renderThread.Start();
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
        _running = false;
        _renderThread?.Join(TimeSpan.FromSeconds(2));
        _renderThread = null;

        lock (_wasmSync)
        {
            _wasmHost?.Dispose();
            _wasmHost = null;
        }

        _renderer?.Dispose();
        _renderer = null;
        Source = null;
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

            var t = (DateTime.UtcNow - _startedUtc).TotalSeconds;
            var pixels = renderer.RenderFrameBgraPixels(t, _camYaw, _camPitch, CamDist);
            if (pixels is not null)
            {
                var snapshot = (byte[])pixels.Clone();
                Dispatcher.UIThread.Post(() => ApplyFrame(snapshot, renderer.Width, renderer.Height));
            }

            lock (_wasmSync)
            {
                _wasmHost?.Tick(t, playing: true);
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

        Source = _displayBitmap;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        _lastPointer = e.GetPosition(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
        {
            return;
        }

        var pos = e.GetPosition(this);
        var delta = pos - _lastPointer;
        _lastPointer = pos;
        _camYaw += (float)(delta.X * 0.01);
        _camPitch = Math.Clamp(_camPitch - (float)(delta.Y * 0.01), -1.4f, 1.4f);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
    }

    public void Dispose() => DetachWorld();
}
