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
    internal static extern bool wgpu_host_render(
        IntPtr handle, float timeSeconds, float eyeX, float eyeY, float eyeZ, float yaw, float pitch);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool wgpu_host_pixels(IntPtr handle, out IntPtr outPtr, out UIntPtr outLen);

    /// <summary>Blittable array params (not raw pointers) — the CLR marshaler pins them for the
    /// duration of the call, so no <c>unsafe</c> context is needed on this side. Indices are
    /// u32 (not u16) — wgpu-host dropped the u16-index-format ceiling on submission size.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool wgpu_host_set_geometry(
        IntPtr handle, float[] vertices, uint vertexCount, uint[] indices, uint indexCount);

    /// <summary>Replaces the shared texture atlas guest geometry's UVs sample against.
    /// <paramref name="rgba"/> is tightly-packed RGBA8, row-major, exactly <c>width * height * 4</c>
    /// bytes.</summary>
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool wgpu_host_set_texture(
        IntPtr handle, byte[] rgba, UIntPtr rgbaLen, uint width, uint height);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint wgpu_host_width(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint wgpu_host_height(IntPtr handle);
}
