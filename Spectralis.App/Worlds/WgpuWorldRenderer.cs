using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

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
        catch (DllNotFoundException)
        {
            IsAvailable = false;
            return null;
        }
        catch (BadImageFormatException)
        {
            // Wrong architecture (e.g. a stale x64 build on an arm64 machine) — same fallback.
            IsAvailable = false;
            return null;
        }

        if (handle == IntPtr.Zero)
        {
            // Native init succeeded but no compatible GPU adapter — not a library problem,
            // don't latch IsAvailable false (a later attempt at a different size could still work).
            return null;
        }

        return new WgpuWorldRenderer(handle, width, height);
    }

    /// <summary>
    /// Renders one frame with a host-driven orbit camera and returns tightly-packed BGRA8
    /// pixels (Avalonia's convention, matching <c>AvaloniaVizCanvas.DrawPixels</c>) — doesn't
    /// touch any Avalonia platform/rendering services, so it's usable from a plain unit test.
    /// The returned array is reused/overwritten every call. Null means the native render or
    /// readback call failed (dropped frame, non-fatal).
    /// </summary>
    public byte[]? RenderFrameBgraPixels(double timeSeconds, float camYaw, float camPitch, float camDist)
    {
        if (_handle == IntPtr.Zero)
        {
            return null;
        }

        if (!WgpuHostNative.wgpu_host_render(_handle, (float)timeSeconds, camYaw, camPitch, camDist))
        {
            return null;
        }

        if (!WgpuHostNative.wgpu_host_pixels(_handle, out var srcPtr, out var srcLenPtr))
        {
            return null;
        }

        var srcLen = (int)srcLenPtr;
        var expected = Width * Height * 4;
        if (srcPtr == IntPtr.Zero || srcLen != expected)
        {
            return null;
        }

        _bgraScratch ??= new byte[expected];
        Marshal.Copy(srcPtr, _bgraScratch, 0, srcLen);
        SwapRedAndBlueInPlace(_bgraScratch);
        return _bgraScratch;
    }

    /// <summary>
    /// Renders one frame (see <see cref="RenderFrameBgraPixels"/>) and returns a bitmap ready
    /// to draw. The same <see cref="WriteableBitmap"/> instance is reused/overwritten every
    /// call — draw it immediately, don't hold onto the reference past the next call. Null means
    /// the native render/readback call failed (dropped frame, non-fatal).
    /// </summary>
    public WriteableBitmap? RenderFrame(double timeSeconds, float camYaw, float camPitch, float camDist)
    {
        var pixels = RenderFrameBgraPixels(timeSeconds, camYaw, camPitch, camDist);
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

    /// <summary>wgpu-host emits RGBA8; Avalonia's WriteableBitmap here is BGRA8 — swap R/B in place.</summary>
    private static void SwapRedAndBlueInPlace(byte[] rgba)
    {
        for (var i = 0; i < rgba.Length; i += 4)
        {
            (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
        }
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
