using System.Runtime.InteropServices;

namespace Spectralis.App.Worlds;

/// <summary>
/// Raw P/Invoke bindings for the <c>wgpu-host</c> Rust cdylib (repo-root <c>wgpu-host/</c>,
/// built by the <c>BuildWgpuHost</c> MSBuild target in Spectralis.App.csproj and copied next
/// to the app as <c>wgpu_host.dll</c>/<c>libwgpu_host.so</c>/<c>libwgpu_host.dylib</c>).
/// Prefer <see cref="WgpuWorldRenderer"/> over calling these directly — it owns lifetime,
/// pixel-format conversion, and the missing-native-library fallback.
/// </summary>
internal static class WgpuHostNative
{
    private const string LibraryName = "wgpu_host";

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wgpu_host_create(uint width, uint height);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wgpu_host_destroy(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool wgpu_host_render(IntPtr handle, float timeSeconds, float camYaw, float camPitch, float camDist);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool wgpu_host_pixels(IntPtr handle, out IntPtr outPtr, out UIntPtr outLen);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint wgpu_host_width(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint wgpu_host_height(IntPtr handle);
}
