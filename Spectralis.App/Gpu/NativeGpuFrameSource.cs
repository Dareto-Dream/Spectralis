using System.Runtime.InteropServices;
using Spectralis.App.Services;
using Spectralis.Core.Common;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Gpu;

/// <summary>
/// The app's <see cref="IGpuFrameSource"/>: drives the native wgpu visualizer pipeline. It keeps one native
/// renderer at a time and rebuilds it when the requested size changes (the picker, a resized window and a
/// video export all ask for different sizes), and it degrades to "unavailable" instead of crashing when the
/// native library is missing or the machine has no usable GPU adapter, which sends the catalog's CPU
/// fallbacks into action.
/// </summary>
public sealed class NativeGpuFrameSource : IGpuFrameSource, IDisposable
{
    private readonly object _gate = new();
    private IntPtr _handle;
    private int _width;
    private int _height;
    private int _builtin = -1;
    private byte[]? _bgra;
    private int _consecutiveFailures;
    private bool _unavailable;
    private bool _libraryChecked;

    /// <summary>
    /// Cheap to ask (it's called whenever a picker is built): true until a load/adapter failure proves
    /// otherwise. It probes only that the library loads, not that a GPU exists, so a machine with the
    /// library but no adapter finds out on its first frame and then flips to false for good.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            lock (_gate)
            {
                if (_unavailable) return false;
                if (_libraryChecked) return true;
                _libraryChecked = true;
                try
                {
                    _unavailable = GpuVizNative.wgpu_viz_builtin_count() == 0;
                }
                catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
                {
                    WasmWorldLog.Log($"GPU visualizers unavailable: {ex.Message}");
                    _unavailable = true;
                }
                return !_unavailable;
            }
        }
    }

    /// <summary>How many built-in visualizers the native library ships (0 when it can't be loaded).</summary>
    public static int NativeBuiltinCount
    {
        get
        {
            try { return (int)GpuVizNative.wgpu_viz_builtin_count(); }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException) { return 0; }
        }
    }

    public byte[]? RenderFrame(
        int builtinIndex, int width, int height, double timeSeconds,
        ReadOnlySpan<float> spectrum, float rms, float peak, VizColor accent)
    {
        if (!IsAvailable) return null;

        lock (_gate)
        {
            try
            {
                if (!EnsureRenderer(width, height)) return null;

                if (builtinIndex != _builtin)
                {
                    if (!GpuVizNative.wgpu_viz_use_builtin(_handle, (uint)builtinIndex)) return Fail("use_builtin rejected the index");
                    _builtin = builtinIndex;
                }

                GpuVizNative.wgpu_viz_set_accent(_handle, accent.R / 255f, accent.G / 255f, accent.B / 255f);
                GpuVizNative.wgpu_viz_set_audio(_handle, spectrum.ToArray(), (uint)spectrum.Length, rms, peak);

                if (!GpuVizNative.wgpu_viz_render(_handle, (float)timeSeconds)) return Fail("render returned false");
                if (!GpuVizNative.wgpu_viz_pixels(_handle, out var ptr, out var len)) return Fail("pixels returned false");

                var expected = _width * _height * 4;
                if (ptr == IntPtr.Zero || (int)len != expected) return Fail($"pixel buffer mismatch ({(int)len} vs {expected})");

                _bgra ??= new byte[expected];
                Marshal.Copy(ptr, _bgra, 0, expected);
                PixelSwizzle.SwapRedBlueInPlace(_bgra); // the GPU hands back RGBA, the canvas wants BGRA
                _consecutiveFailures = 0;
                return _bgra;
            }
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {
                WasmWorldLog.Log($"GPU visualizer native call failed: {ex.Message}");
                _unavailable = true;
                return null;
            }
        }
    }

    private bool EnsureRenderer(int width, int height)
    {
        if (_handle != IntPtr.Zero && width == _width && height == _height) return true;

        ReleaseRenderer();
        var handle = GpuVizNative.wgpu_viz_create((uint)width, (uint)height);
        if (handle == IntPtr.Zero)
        {
            // The library loaded but there's no usable GPU adapter: don't keep retrying every frame.
            WasmWorldLog.Log($"GPU visualizers unavailable: no compatible GPU adapter ({width}x{height}).");
            _unavailable = true;
            return false;
        }

        _handle = handle;
        _width = width;
        _height = height;
        _builtin = -1; // a fresh renderer starts on built-in 0
        _bgra = null;
        return true;
    }

    private byte[]? Fail(string reason)
    {
        // A dropped frame is normal (a size change mid-render); a streak means the GPU is really broken.
        _consecutiveFailures++;
        if (_consecutiveFailures <= 3) WasmWorldLog.Log($"GPU visualizer frame dropped: {reason}");
        if (_consecutiveFailures >= 30) _unavailable = true;
        return null;
    }

    private void ReleaseRenderer()
    {
        if (_handle != IntPtr.Zero)
        {
            GpuVizNative.wgpu_viz_destroy(_handle);
            _handle = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        lock (_gate) ReleaseRenderer();
    }
}
