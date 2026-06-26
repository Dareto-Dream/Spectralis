using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Spectralis.App.Services;

public enum AppThemeMode
{
    Dark,
    Light,
    Oled,
    Midnight,
}

public enum AppThemeAccent
{
    Amber,
    Ocean,
    Rose,
    Forest,
    Violet,
    Crimson,
    Cyan,
    Mint,
    Sunset,
    Gold,
}

public sealed record AppThemePalette(
    bool IsDark,
    Color Base,
    Color Raised,
    Color Overlay,
    Color PrimaryText,
    Color SecondaryText,
    Color MutedText,
    Color Signal,
    Color SignalDim,
    Color Border,
    Color Hover,
    Color Pressed,
    Color Selection);

public static class AppThemeService
{
    public static void Apply(AppSettings settings) =>
        Apply(settings.ThemeMode, settings.ThemeAccent);

    public static void Apply(AppThemeMode mode, AppThemeAccent accent)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var palette = CreatePalette(mode, accent);
        app.RequestedThemeVariant = palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

        SetBrush(app, "Brush.Bg.Base", palette.Base);
        SetBrush(app, "Brush.Bg.Raised", palette.Raised);
        SetBrush(app, "Brush.Bg.Overlay", palette.Overlay);
        SetBrush(app, "Brush.Ink.Primary", palette.PrimaryText);
        SetBrush(app, "Brush.Ink.Secondary", palette.SecondaryText);
        SetBrush(app, "Brush.Ink.Muted", palette.MutedText);
        SetBrush(app, "Brush.Signal", palette.Signal);
        SetBrush(app, "Brush.Signal.Dim", palette.SignalDim);
        SetBrush(app, "Brush.Border", palette.Border);
        SetBrush(app, "Brush.Hover", palette.Hover);
        SetBrush(app, "Brush.Pressed", palette.Pressed);
        SetBrush(app, "Brush.Selection", palette.Selection);
    }

    private static AppThemePalette CreatePalette(AppThemeMode mode, AppThemeAccent accent)
    {
        var (signal, signalDim) = GetAccentColors(accent);
        var isDark = mode != AppThemeMode.Light;

        var baseColor = mode switch
        {
            AppThemeMode.Light => Color.FromRgb(244, 239, 233),
            AppThemeMode.Oled => Color.FromRgb(2, 2, 2),
            AppThemeMode.Midnight => Color.FromRgb(8, 10, 24),
            _ => Color.FromRgb(23, 20, 24),
        };
        var raised = mode switch
        {
            AppThemeMode.Light => Color.FromRgb(255, 252, 248),
            AppThemeMode.Oled => Color.FromRgb(12, 10, 14),
            AppThemeMode.Midnight => Color.FromRgb(13, 16, 35),
            _ => Color.FromRgb(31, 27, 33),
        };
        var overlay = mode switch
        {
            AppThemeMode.Light => Color.FromRgb(237, 229, 220),
            AppThemeMode.Oled => Color.FromRgb(20, 18, 22),
            AppThemeMode.Midnight => Color.FromRgb(20, 24, 50),
            _ => Color.FromRgb(40, 35, 43),
        };
        var primaryText = mode switch
        {
            AppThemeMode.Light => Color.FromRgb(31, 23, 17),
            AppThemeMode.Oled => Color.FromRgb(248, 244, 240),
            AppThemeMode.Midnight => Color.FromRgb(218, 226, 255),
            _ => Color.FromRgb(245, 238, 229),
        };
        var secondaryText = mode switch
        {
            AppThemeMode.Light => Color.FromRgb(103, 87, 74),
            AppThemeMode.Oled => Color.FromRgb(185, 170, 158),
            AppThemeMode.Midnight => Color.FromRgb(140, 158, 210),
            _ => Color.FromRgb(189, 172, 157),
        };
        var mutedText = mode switch
        {
            AppThemeMode.Light => Color.FromRgb(151, 137, 125),
            AppThemeMode.Oled => Color.FromRgb(118, 108, 100),
            AppThemeMode.Midnight => Color.FromRgb(90, 108, 162),
            _ => Color.FromRgb(132, 120, 111),
        };
        var border = mode switch
        {
            AppThemeMode.Light => Color.FromRgb(206, 191, 177),
            AppThemeMode.Oled => Color.FromRgb(55, 45, 38),
            AppThemeMode.Midnight => Color.FromRgb(52, 65, 135),
            _ => Color.FromRgb(87, 71, 61),
        };

        return new AppThemePalette(
            isDark,
            baseColor,
            raised,
            overlay,
            primaryText,
            secondaryText,
            mutedText,
            signal,
            signalDim,
            border,
            Blend(raised, overlay, 0.48),
            Blend(overlay, signal, isDark ? 0.12 : 0.08),
            WithAlpha(signal, (byte)(isDark ? 42 : 58)));
    }

    private static (Color Primary, Color Secondary) GetAccentColors(AppThemeAccent accent) =>
        accent switch
        {
            AppThemeAccent.Ocean => (Color.FromRgb(92, 163, 255), Color.FromRgb(55, 112, 214)),
            AppThemeAccent.Rose => (Color.FromRgb(231, 138, 167), Color.FromRgb(206, 95, 129)),
            AppThemeAccent.Forest => (Color.FromRgb(128, 193, 139), Color.FromRgb(67, 140, 102)),
            AppThemeAccent.Violet => (Color.FromRgb(168, 130, 242), Color.FromRgb(118, 76, 210)),
            AppThemeAccent.Crimson => (Color.FromRgb(222, 82, 102), Color.FromRgb(178, 44, 70)),
            AppThemeAccent.Cyan => (Color.FromRgb(68, 202, 222), Color.FromRgb(28, 156, 182)),
            AppThemeAccent.Mint => (Color.FromRgb(98, 212, 176), Color.FromRgb(44, 170, 136)),
            AppThemeAccent.Sunset => (Color.FromRgb(252, 132, 68), Color.FromRgb(220, 76, 44)),
            AppThemeAccent.Gold => (Color.FromRgb(232, 192, 58), Color.FromRgb(200, 148, 24)),
            _ => (Color.FromRgb(244, 176, 87), Color.FromRgb(224, 114, 83)),
        };

    private static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            255,
            (byte)Math.Round(from.R + ((to.R - from.R) * amount)),
            (byte)Math.Round(from.G + ((to.G - from.G) * amount)),
            (byte)Math.Round(from.B + ((to.B - from.B) * amount)));
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static void SetBrush(Application app, string key, Color color)
    {
        if (app.Resources.TryGetResource(key, null, out var value) &&
            value is SolidColorBrush brush)
        {
            brush.Color = color;
            return;
        }

        app.Resources[key] = new SolidColorBrush(color);
    }
}
