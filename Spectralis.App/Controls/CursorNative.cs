using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Spectralis.App.Controls;

/// <summary>
/// The two Win32 calls <see cref="WgpuWorldSurface"/>'s pointer lock needs that Avalonia doesn't
/// expose: reading and warping the OS cursor's raw screen position. Avalonia's own <c>Cursor</c>
/// property handles hiding the cursor image (cross-platform); this handles the "never let it hit
/// the screen edge and stop generating move deltas" trick real pointer-lock APIs do internally —
/// by polling the raw position once per render frame and warping back to center, entirely outside
/// Avalonia's own pointer-event pipeline (see <see cref="WgpuWorldSurface"/>'s per-frame poll for
/// why: driving this from <c>OnPointerMoved</c> instead means every warp can itself deliver a new
/// move event, which can retrigger another warp — a feedback loop, confirmed as a slow constant-
/// rate camera drift with the physical mouse sitting still). Windows-only — pointer lock degrades
/// to "still works, but look stops at the screen edge" everywhere else rather than crashing or
/// throwing PlatformNotSupportedException.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class CursorNative
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    internal static void RecenterTo(int screenX, int screenY) => SetCursorPos(screenX, screenY);

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint milliseconds);

    /// <summary>Asks Windows for ~1ms timer granularity while the render loop is paced. At the
    /// default ~15.6ms a Sleep(16) can land on 31ms, which would quietly turn a 60fps cap into 30.
    /// Always pair with <see cref="EndFineTimer"/>.</summary>
    internal static void BeginFineTimer()
    {
        try { timeBeginPeriod(1); } catch { /* winmm missing: just coarser pacing */ }
    }

    internal static void EndFineTimer()
    {
        try { timeEndPeriod(1); } catch { }
    }

    /// <summary>Raw OS cursor position in screen pixels. Returns false (out params zeroed) on
    /// the rare native failure — callers should treat that as "no movement this frame", not throw.</summary>
    internal static bool TryGetScreenPos(out int screenX, out int screenY)
    {
        if (GetCursorPos(out var point))
        {
            screenX = point.X;
            screenY = point.Y;
            return true;
        }

        screenX = 0;
        screenY = 0;
        return false;
    }
}
