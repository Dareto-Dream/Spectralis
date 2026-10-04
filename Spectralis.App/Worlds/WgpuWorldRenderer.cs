using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Spectralis.App.Services;
using Spectralis.Core.Common;

namespace Spectralis.App.Worlds;

/// <summary>
/// Managed wrapper around the <c>wgpu-host</c> native renderer: owns the native handle's
/// lifetime, converts its RGBA8 pixel buffer into an Avalonia <see cref="WriteableBitmap"/>
/// (BGRA8, matching <c>AvaloniaVizCanvas.DrawPixels</c>'s convention), and degrades to
/// "unavailable" instead of crashing when the native library is missing (e.g. a machine
/// without the Rust toolchain that built it) or no compatible GPU adapter exists.
/// </summary>
public sealed class WgpuWorldRenderer : IDisposable
{
    private IntPtr _handle;
    private byte[]? _bgraScratch;
    private WriteableBitmap? _bitmap;
    private int _consecutiveDroppedFrames;

    private WgpuWorldRenderer(IntPtr handle, int width, int height)
    {
        _handle = handle;
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// True once a native call has succeeded in this process — a cheap way for UI code to
    /// decide whether to even attempt <see cref="Create"/> after the first failure, without
    /// re-triggering a DllNotFoundException probe every time.
    /// </summary>
    public static bool IsAvailable { get; private set; } = true;

    /// <summary>
    /// Creates a renderer targeting <paramref name="width"/>x<paramref name="height"/>, or
    /// returns null if the native library isn't present or no compatible GPU adapter exists.
    /// Never throws for those two expected-in-the-wild cases.
    /// </summary>
    public static WgpuWorldRenderer? Create(int width, int height)
    {
        if (!IsAvailable)
        {
            return null;
        }

        // The native side clamps 0 (or negative, cast away below) up to 1x1 rather than
        // failing — mirror that here so Width/Height (and the pixel-buffer size checks in
        // RenderFrameBgraPixels) agree with what actually got allocated.
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        IntPtr handle;
        try
        {
            handle = WgpuHostNative.wgpu_host_create((uint)width, (uint)height);
        }
        catch (DllNotFoundException ex)
        {
            WasmWorldLog.Log($"WgpuWorldRenderer.Create({width}x{height}): native library not found — {ex.Message}");
            IsAvailable = false;
            return null;
        }
        catch (BadImageFormatException ex)
        {
            // Wrong architecture (e.g. a stale x64 build on an arm64 machine) — same fallback.
            WasmWorldLog.Log($"WgpuWorldRenderer.Create({width}x{height}): wrong native library architecture — {ex.Message}");
            IsAvailable = false;
            return null;
        }

        if (handle == IntPtr.Zero)
        {
            // Native init succeeded but no compatible GPU adapter — not a library problem,
            // don't latch IsAvailable false (a later attempt at a different size could still work).
            WasmWorldLog.Log($"WgpuWorldRenderer.Create({width}x{height}): native init returned null handle — no compatible GPU adapter");
            return null;
        }

        WasmWorldLog.Log($"WgpuWorldRenderer.Create({width}x{height}): ok, handle=0x{handle:X}");
        return new WgpuWorldRenderer(handle, width, height);
    }

    /// <summary>
    /// Renders one frame through a positional camera — <paramref name="eyeX"/>/<paramref name="eyeY"/>/
    /// <paramref name="eyeZ"/> is world-space eye position, <paramref name="yaw"/>/<paramref name="pitch"/>
    /// (radians) is look direction — and returns tightly-packed BGRA8 pixels (Avalonia's
    /// convention, matching <c>AvaloniaVizCanvas.DrawPixels</c>). The pose normally comes from
    /// the loaded world's own <c>set_camera_pose</c> calls (see <see cref="Spectralis.Core.Worlds.WasmWorldHost"/>),
    /// not from anything this class tracks itself. Doesn't touch any Avalonia platform/rendering
    /// services, so it's usable from a plain unit test. The returned array is reused/overwritten
    /// every call. Null means the native render or readback call failed (dropped frame, non-fatal).
    /// </summary>
    public byte[]? RenderFrameBgraPixels(double timeSeconds, float eyeX, float eyeY, float eyeZ, float yaw, float pitch)
    {
        if (_handle == IntPtr.Zero)
        {
            return null;
        }

        if (!WgpuHostNative.wgpu_host_render(_handle, (float)timeSeconds, eyeX, eyeY, eyeZ, yaw, pitch))
        {
            LogDroppedFrame("wgpu_host_render returned false");
            return null;
        }

        if (!WgpuHostNative.wgpu_host_pixels(_handle, out var srcPtr, out var srcLenPtr))
        {
            LogDroppedFrame("wgpu_host_pixels returned false");
            return null;
        }

        var srcLen = (int)srcLenPtr;
        var expected = Width * Height * 4;
        if (srcPtr == IntPtr.Zero || srcLen != expected)
        {
            LogDroppedFrame($"pixel buffer mismatch: srcPtr=0x{srcPtr:X} srcLen={srcLen} expected={expected}");
            return null;
        }

        _consecutiveDroppedFrames = 0;
        _bgraScratch ??= new byte[expected];
        Marshal.Copy(srcPtr, _bgraScratch, 0, srcLen);
        PixelSwizzle.SwapRedBlueInPlace(_bgraScratch);
        return _bgraScratch;
    }

    /// <summary>Logs a dropped frame — every one of the first 5 in a row, then only every 120th
    /// (~once every couple seconds at 60fps) so a renderer that's failing every single call
    /// doesn't flood the log file, while still making "it's still failing" visible over time.</summary>
    private void LogDroppedFrame(string reason)
    {
        _consecutiveDroppedFrames++;
        if (_consecutiveDroppedFrames <= 5 || _consecutiveDroppedFrames % 120 == 0)
        {
            WasmWorldLog.Log($"RenderFrameBgraPixels: dropped frame #{_consecutiveDroppedFrames} in a row — {reason}");
        }
    }

    /// <summary>
    /// Renders one frame (see <see cref="RenderFrameBgraPixels"/>) and returns a bitmap ready
    /// to draw. The same <see cref="WriteableBitmap"/> instance is reused/overwritten every
    /// call — draw it immediately, don't hold onto the reference past the next call. Null means
    /// the native render/readback call failed (dropped frame, non-fatal).
    /// </summary>
    public WriteableBitmap? RenderFrame(double timeSeconds, float eyeX, float eyeY, float eyeZ, float yaw, float pitch)
    {
        var pixels = RenderFrameBgraPixels(timeSeconds, eyeX, eyeY, eyeZ, yaw, pitch);
        if (pixels is null)
        {
            return null;
        }

        _bitmap ??= new WriteableBitmap(
            new PixelSize(Width, Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var buffer = _bitmap.Lock())
        {
            Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }

        return _bitmap;
    }

    /// <summary>
    /// Replaces the rendered scene's geometry with guest-submitted vertices/indices — the
    /// wasm-side counterpart of the built-in test cube. <paramref name="interleavedVertices"/>
    /// is <c>[f32;3] position, [f32;2] uv, [f32;3] color</c> repeated per vertex (8 floats/vertex,
    /// matching wgpu-host's <c>Vertex</c> layout). A world that never calls <see cref="SetTexture"/>
    /// still renders flat per-vertex color (the default atlas is a 1x1 white pixel, a no-op
    /// multiply). Returns false (geometry left unchanged — still whatever it was before, cube or
    /// an earlier valid submission) for empty input or anything past wgpu-host's fixed caps
    /// (500000 vertices, 1500000 indices) rather than throwing; a malformed wasm world shouldn't
    /// be able to crash the renderer, only fail to draw.
    /// </summary>
    public bool SubmitGeometry(ReadOnlySpan<float> interleavedVertices, ReadOnlySpan<uint> indices)
    {
        if (_handle == IntPtr.Zero)
        {
            return false;
        }

        const int floatsPerVertex = 8;
        if (interleavedVertices.Length == 0 || indices.Length == 0 || interleavedVertices.Length % floatsPerVertex != 0)
        {
            return false;
        }

        var vertexCount = interleavedVertices.Length / floatsPerVertex;
        return WgpuHostNative.wgpu_host_set_geometry(
            _handle, interleavedVertices.ToArray(), (uint)vertexCount, indices.ToArray(), (uint)indices.Length);
    }

    /// <summary>
    /// Replaces the shared texture atlas guest geometry's <c>uv</c> attribute samples against.
    /// <paramref name="rgba"/> must be exactly <paramref name="width"/> * <paramref name="height"/>
    /// * 4 bytes, tightly-packed RGBA8, row-major. Returns false (atlas left unchanged) for empty
    /// input, a dimension mismatch, or anything past wgpu-host's fixed cap (4096 per side) rather
    /// than throwing, same non-fatal-on-malformed-input contract as <see cref="SubmitGeometry"/>.
    /// </summary>
    public bool SetTexture(ReadOnlySpan<byte> rgba, uint width, uint height)
    {
        if (_handle == IntPtr.Zero)
        {
            return false;
        }

        if (rgba.Length == 0 || rgba.Length != (long)width * height * 4)
        {
            return false;
        }

        var bytes = rgba.ToArray();
        return WgpuHostNative.wgpu_host_set_texture(_handle, bytes, (UIntPtr)bytes.Length, width, height);
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            WgpuHostNative.wgpu_host_destroy(_handle);
            _handle = IntPtr.Zero;
        }

        _bitmap?.Dispose();
        _bitmap = null;
    }
}
