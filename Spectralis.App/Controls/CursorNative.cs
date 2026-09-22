using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Spectralis.App.Controls;

/// <summary>
/// The one Win32 call <see cref="WgpuWorldSurface"/>'s pointer lock needs that Avalonia doesn't
/// expose: warping the OS cursor back to a screen point. Avalonia's own <c>Cursor</c> property
/// handles hiding the cursor image (cross-platform); this handles the "never let it hit the
/// screen edge and stop generating move deltas" trick real pointer-lock APIs do internally.
/// Windows-only — pointer lock degrades to "still works, but look stops at the screen edge"
/// everywhere else rather than crashing or throwing PlatformNotSupportedException.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class CursorNative
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    internal static void RecenterTo(int screenX, int screenY) => SetCursorPos(screenX, screenY);
}
