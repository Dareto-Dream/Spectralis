using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    private DispatcherTimer? _renderTimer;
    private DateTime _startedUtc;

    private float _camYaw;
    private float _camPitch = 0.3f;
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
            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _renderTimer.Tick += (_, _) => OnRenderTick();
            _renderTimer.Start();
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
        Log(_wasmHost.Load(wasmBytes) ? "test world loaded" : "test world FAILED to load");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _renderTimer?.Stop();
        _renderTimer = null;
        _wasmHost?.Dispose();
        _wasmHost = null;
        _renderer?.Dispose();
        _renderer = null;
    }

    private void OnRenderTick()
    {
        if (_renderer is null)
        {
            return;
        }

        var t = (DateTime.UtcNow - _startedUtc).TotalSeconds;
        var bitmap = _renderer.RenderFrame(t, _camYaw, _camPitch, CamDist);
        if (bitmap is not null)
        {
            Surface.Source = bitmap;
        }

        _wasmHost?.Tick(t, playing: true);
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

    private void OnRequestSwitchToHtml(object? sender, RoutedEventArgs e) =>
        _wasmHost?.TriggerExport("request_exit_to_html");

    private void Log(string message) =>
        Dispatcher.UIThread.Post(() => EventLog.Text = $"{DateTime.Now:HH:mm:ss.fff}  {message}\n{EventLog.Text}");
}
