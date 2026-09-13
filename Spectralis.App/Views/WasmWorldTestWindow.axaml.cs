using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Spectralis.App.Worlds;
using Spectralis.Core.Worlds;
using Wasmtime;

namespace Spectralis.App.Views;

/// <summary>
/// Standalone proof rig for Phase 2 of the dual-runtime Album Worlds rework — NOT wired into
/// the real capsule-loading pipeline (that needs a manifest/package format extension for
/// shipping a .wasm module + assets, tracked as follow-up). This window exists to show every
/// piece already built running together, live: the wgpu-host native renderer composited into
/// an Avalonia surface with a mouse-driven orbit camera, and a sandboxed wasmtime test world
/// (compiled from inline WAT — no wasm32 toolchain needed for this proof) whose on_load fires
/// play_track/unlock_achievement/register_dsp_preset host imports, and whose
/// request_exit_to_html export exercises the Wasm-&gt;HTML symmetric hand-off hook.
///
/// Rendering runs on a dedicated background thread, not a DispatcherTimer. wgpu_host_render
/// does a synchronous GPU submit + blocking device.poll + buffer-map round trip every call —
/// cheap in absolute terms, but running it on Avalonia's single UI thread meant every render
/// call blocked pointer-input processing behind it, so a drag felt like it landed a full frame
/// (or several queued frames) late. The render loop now only touches the UI thread to hand off
/// a finished bitmap via Dispatcher.UIThread.Post; camera state is read with plain volatile
/// fields (torn reads of a single float aren't possible on .NET, and a one-frame-stale angle is
/// harmless here).
/// </summary>
public partial class WasmWorldTestWindow : Window
{
    private const string TestWorldWat = """
        (module
          (import "spectral" "play_track" (func $play_track (param i32 i32 f64)))
          (import "spectral" "unlock_achievement" (func $unlock_achievement (param i32 i32)))
          (import "spectral" "register_dsp_preset" (func $register_dsp_preset (param i32 i32)))
          (import "spectral" "switch_to_html" (func $switch_to_html (param i32 i32)))
          (memory (export "memory") 1)
          (data (i32.const 0) "the-line")
          (data (i32.const 16) "entered-the-world")
          (data (i32.const 64) "{\22Enabled\22:true,\22Effects\22:[{\22Name\22:\22Stereo Widener\22,\22Enabled\22:true,\22Params\22:{\22amount\22:0.5}}]}")
          (data (i32.const 200) "story.html")
          (func (export "on_load")
            (call $play_track (i32.const 0) (i32.const 8) (f64.const 0.0))
            (call $unlock_achievement (i32.const 16) (i32.const 17))
            (call $register_dsp_preset (i32.const 64) (i32.const 93)))
          (func (export "request_exit_to_html")
            (call $switch_to_html (i32.const 200) (i32.const 10))))
        """;

    private WgpuWorldRenderer? _renderer;
    private WasmWorldHost? _wasmHost;

    // Guards every call into _wasmHost — the render thread's per-frame Tick() and the UI
    // thread's button-triggered TriggerExport() must never run concurrently against the same
    // wasmtime Store, which (like most wasm runtimes) isn't safe to call into from two threads
    // at once without external synchronization.
    private readonly object _wasmSync = new();

    private Thread? _renderThread;
    private volatile bool _running;
    private DateTime _startedUtc;

    private volatile float _camYaw;
    private volatile float _camPitch = 0.3f;
    private const float CamDist = 3.2f;

    private bool _dragging;
    private Point _lastPointer;

    public WasmWorldTestWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _startedUtc = DateTime.UtcNow;

        _renderer = WgpuWorldRenderer.Create(640, 480);
        if (_renderer is null)
        {
            UnavailableLabel.IsVisible = true;
        }
        else
        {
            _running = true;
            _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "WasmWorldTestRig-Render" };
            _renderThread.Start();
        }

        _wasmHost = new WasmWorldHost("wasm-world-test-rig");
        _wasmHost.PlayTrackRequested += (_, req) => Log($"play_track(\"{req.TrackId}\", {req.PositionSeconds})");
        _wasmHost.AchievementUnlocked += (_, id) => Log($"unlock_achievement(\"{id}\")");
        _wasmHost.DspPresetRegisterRequested += (_, req) => Log($"register_dsp_preset({req.PresetChainJson})");
        _wasmHost.DspPresetReleaseRequested += (_, _) => Log("release_dsp_preset()");
        _wasmHost.SwitchToHtmlRequested += (_, entry) =>
        {
            Log($"switch_to_html(\"{entry}\") — closing (a real coordinator would swap surfaces here)");
            Dispatcher.UIThread.Post(Close);
        };

        var wasmBytes = Module.ConvertText(TestWorldWat);
        bool loaded;
        lock (_wasmSync)
        {
            loaded = _wasmHost.Load(wasmBytes);
        }
        Log(loaded ? "test world loaded" : "test world FAILED to load");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _running = false;
        _renderThread?.Join(TimeSpan.FromSeconds(2));
        _renderThread = null;

        _wasmHost?.Dispose();
        _wasmHost = null;
        _renderer?.Dispose();
        _renderer = null;
    }

    /// <summary>Runs entirely off the UI thread — only the final Image.Source assignment hops back on.</summary>
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
                // Snapshot before handing off — the next loop iteration overwrites the
                // renderer's shared scratch buffer as soon as we call RenderFrameBgraPixels again.
                var snapshot = (byte[])pixels.Clone();
                Dispatcher.UIThread.Post(() => ApplyFrame(snapshot, renderer.Width, renderer.Height));
            }

            lock (_wasmSync)
            {
                _wasmHost?.Tick(t, playing: true);
            }
        }
    }

    private WriteableBitmap? _displayBitmap;

    private void ApplyFrame(byte[] bgraPixels, int width, int height)
    {
        if (!_running)
        {
            return;
        }

        if (_displayBitmap is null || _displayBitmap.PixelSize.Width != width || _displayBitmap.PixelSize.Height != height)
        {
            _displayBitmap?.Dispose();
            _displayBitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                Avalonia.Platform.AlphaFormat.Premul);
        }

        using (var buffer = _displayBitmap.Lock())
        {
            System.Runtime.InteropServices.Marshal.Copy(bgraPixels, 0, buffer.Address, bgraPixels.Length);
        }

        Surface.Source = _displayBitmap;
    }

    private void OnSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragging = true;
        _lastPointer = e.GetPosition(Surface);
    }

    private void OnSurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var pos = e.GetPosition(Surface);
        var delta = pos - _lastPointer;
        _lastPointer = pos;
        _camYaw += (float)(delta.X * 0.01);
        _camPitch = Math.Clamp(_camPitch - (float)(delta.Y * 0.01), -1.4f, 1.4f);
    }

    private void OnSurfacePointerReleased(object? sender, PointerReleasedEventArgs e) => _dragging = false;

    private void OnRequestSwitchToHtml(object? sender, RoutedEventArgs e)
    {
        lock (_wasmSync)
        {
            _wasmHost?.TriggerExport("request_exit_to_html");
        }
    }

    private void Log(string message) =>
        Dispatcher.UIThread.Post(() => EventLog.Text = $"{DateTime.Now:HH:mm:ss.fff}  {message}\n{EventLog.Text}");
}
