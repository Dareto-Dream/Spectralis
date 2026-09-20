using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Wasmtime;

namespace Spectralis.App.Views;

/// <summary>
/// Standalone proof rig for Phase 2 of the dual-runtime Album Worlds rework — NOT wired into
/// the real capsule-loading pipeline (that needs a manifest/package format extension for
/// shipping a .wasm module + assets, tracked as follow-up). This window exists to show every
/// piece already built running together, live: <see cref="Controls.WgpuWorldSurface"/>
/// (the same control the real album-world surface will use) rendering a sandboxed wasmtime test
/// world (compiled from inline WAT — no wasm32 toolchain needed for this proof) whose on_load
/// fires play_track/unlock_achievement/register_dsp_preset host imports, whose on_input walks a
/// positional camera around via set_camera_pose (proving the WASD/mouselook -&gt; guest -&gt;
/// wgpu-host loop end to end — unlocking "touched-the-cube" on interact stands in for a real
/// world's "walked up to an object" beat), and whose request_exit_to_html export exercises the
/// Wasm-&gt;HTML symmetric hand-off hook.
///
/// The test world's movement is intentionally world-space (moveForward/moveRight add straight to
/// Z/X, ignoring its own accumulated yaw) rather than facing-relative — real facing-relative
/// movement needs sin/cos of yaw to rotate the input axes, and WebAssembly has no trig
/// instructions; a real Rust-authored world (via spectralis-world-sdk's rust-minimal template)
/// would pull in a math crate for that. Good enough here to prove the plumbing, not the feel.
/// </summary>
public partial class WasmWorldTestWindow : Window
{
    private const string TestWorldWat = """
        (module
          (import "spectral" "play_track" (func $play_track (param i32 i32 f64)))
          (import "spectral" "unlock_achievement" (func $unlock_achievement (param i32 i32)))
          (import "spectral" "register_dsp_preset" (func $register_dsp_preset (param i32 i32)))
          (import "spectral" "switch_to_html" (func $switch_to_html (param i32 i32)))
          (import "spectral" "set_camera_pose" (func $set_camera_pose (param f64 f64 f64 f64 f64)))
          (memory (export "memory") 1)
          (data (i32.const 0) "the-line")
          (data (i32.const 16) "entered-the-world")
          (data (i32.const 64) "{\22Enabled\22:true,\22Effects\22:[{\22Name\22:\22Stereo Widener\22,\22Enabled\22:true,\22Params\22:{\22amount\22:0.5}}]}")
          (data (i32.const 200) "story.html")
          (data (i32.const 220) "touched-the-cube")

          (global $posX (mut f64) (f64.const 0))
          (global $posZ (mut f64) (f64.const 4))
          (global $yaw (mut f64) (f64.const 3.14159265358979))
          (global $pitch (mut f64) (f64.const 0))

          (func (export "on_load")
            (call $play_track (i32.const 0) (i32.const 8) (f64.const 0.0))
            (call $unlock_achievement (i32.const 16) (i32.const 17))
            (call $register_dsp_preset (i32.const 64) (i32.const 93))
            (call $set_camera_pose
              (global.get $posX) (f64.const 1.6) (global.get $posZ)
              (global.get $yaw) (global.get $pitch)))

          (func (export "on_input")
                (param $moveForward f64) (param $moveRight f64)
                (param $lookYaw f64) (param $lookPitch f64) (param $interact i32)
            (global.set $posZ
              (f64.sub (global.get $posZ) (f64.mul (local.get $moveForward) (f64.const 2.0))))
            (global.set $posX
              (f64.add (global.get $posX) (f64.mul (local.get $moveRight) (f64.const 2.0))))
            (global.set $yaw (f64.add (global.get $yaw) (local.get $lookYaw)))
            (global.set $pitch (f64.add (global.get $pitch) (local.get $lookPitch)))
            (call $set_camera_pose
              (global.get $posX) (f64.const 1.6) (global.get $posZ)
              (global.get $yaw) (global.get $pitch))
            (if (i32.ne (local.get $interact) (i32.const 0))
              (then (call $unlock_achievement (i32.const 220) (i32.const 16)))))

          (func (export "request_exit_to_html")
            (call $switch_to_html (i32.const 200) (i32.const 10))))
        """;

    public WasmWorldTestWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (!Surface.IsWasmAvailable)
        {
            UnavailableLabel.IsVisible = true;
            return;
        }

        Surface.PlayTrackRequested += (_, req) => Log($"play_track(\"{req.TrackId}\", {req.PositionSeconds})");
        Surface.AchievementUnlocked += (_, id) => Log($"unlock_achievement(\"{id}\")");
        Surface.DspPresetRegisterRequested += (_, req) => Log($"register_dsp_preset({req.PresetChainJson})");
        Surface.DspPresetReleaseRequested += (_, _) => Log("release_dsp_preset()");
        Surface.SwitchToHtmlRequested += (_, entry) =>
        {
            Log($"switch_to_html(\"{entry}\") — closing (a real coordinator would swap surfaces here)");
            Dispatcher.UIThread.Post(Close);
        };

        var wasmBytes = Module.ConvertText(TestWorldWat);
        var attached = Surface.AttachWorld(wasmBytes, "wasm-world-test-rig", 640, 480);
        Log(attached ? "test world loaded" : "test world FAILED to load");
        if (!attached)
        {
            UnavailableLabel.IsVisible = true;
        }
    }

    private void OnClosed(object? sender, EventArgs e) => Surface.Dispose();

    private void OnRequestSwitchToHtml(object? sender, RoutedEventArgs e) =>
        Surface.TriggerExport("request_exit_to_html");

    private void Log(string message) =>
        Dispatcher.UIThread.Post(() => EventLog.Text = $"{DateTime.Now:HH:mm:ss.fff}  {message}\n{EventLog.Text}");
}
