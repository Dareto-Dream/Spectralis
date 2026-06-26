using System.Drawing;
using System.Runtime.InteropServices;

namespace Spectralis;

internal static class WindowChromeStyler
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    public static void ApplyTheme(Form form, ThemePalette palette)
    {
        if (!form.IsHandleCreated)
            return;

        try
        {
            var useDarkMode = palette.IsDark ? 1 : 0;
            TrySetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, useDarkMode);
            TrySetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, useDarkMode);
            TrySetWindowAttribute(form.Handle, DWMWA_CAPTION_COLOR, ColorTranslator.ToWin32(palette.WindowBackColor));
            TrySetWindowAttribute(form.Handle, DWMWA_TEXT_COLOR, ColorTranslator.ToWin32(palette.TextPrimaryColor));
            TrySetWindowAttribute(form.Handle, DWMWA_BORDER_COLOR, ColorTranslator.ToWin32(palette.BorderStrongColor));
        }
        catch
        {
            // Ignore best-effort chrome theming failures on unsupported Windows builds.
        }
    }

    private static void TrySetWindowAttribute(IntPtr handle, int attribute, int value)
    {
        _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
