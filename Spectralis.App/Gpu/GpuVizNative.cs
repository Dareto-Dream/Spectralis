using System.Runtime.InteropServices;

namespace Spectralis.App.Gpu;

/// <summary>
/// Raw P/Invoke bindings for the visualizer half of the <c>wgpu-host</c> Rust cdylib (wgpu-host/src/viz.rs).
/// Same library as the world renderer, built by the same <c>BuildWgpuHost</c> target. Prefer
/// <see cref="NativeGpuFrameSource"/>, which owns handles and the missing-library fallback.
/// </summary>
internal static class GpuVizNative
{
    private const string LibraryName = "wgpu_host";

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wgpu_viz_create(uint width, uint height);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wgpu_viz_destroy(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint wgpu_viz_builtin_count();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool wgpu_viz_use_builtin(IntPtr handle, uint index);

    /// <summary>UTF-8 WGSL containing <c>fn viz(uv: vec2&lt;f32&gt;) -&gt; vec4&lt;f32&gt;</c>.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool wgpu_viz_set_shader(IntPtr handle, byte[] wgsl, UIntPtr wgslLen);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool wgpu_viz_last_error(IntPtr handle, out IntPtr outPtr, out UIntPtr outLen);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool wgpu_viz_set_audio(IntPtr handle, float[] levels, uint count, float rms, float peak);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool wgpu_viz_set_accent(IntPtr handle, float r, float g, float b);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool wgpu_viz_render(IntPtr handle, float timeSeconds);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool wgpu_viz_pixels(IntPtr handle, out IntPtr outPtr, out UIntPtr outLen);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint wgpu_viz_width(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint wgpu_viz_height(IntPtr handle);
}
