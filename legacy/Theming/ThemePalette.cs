using System.Drawing;

namespace Spectralis;

internal sealed class ThemePalette
{
    public required bool IsDark { get; init; }
    public required Color WindowBackColor { get; init; }
    public required Color SurfaceBackColor { get; init; }
    public required Color SurfaceAltBackColor { get; init; }
    public required Color SurfaceRaisedColor { get; init; }
    public required Color TextPrimaryColor { get; init; }
    public required Color TextSecondaryColor { get; init; }
    public required Color TextSoftColor { get; init; }
    public required Color TextMutedColor { get; init; }
    public required Color AccentPrimaryColor { get; init; }
    public required Color AccentSecondaryColor { get; init; }
    public required Color AccentSoftColor { get; init; }
    public required Color AccentContrastColor { get; init; }
    public required Color DangerColor { get; init; }
    public required Color DangerTextColor { get; init; }
    public required Color BorderColor { get; init; }
    public required Color BorderStrongColor { get; init; }

    public static ThemePalette Create(ThemeMode mode, ThemeAccent accent)
    {
        var (accentPrimary, accentSecondary) = GetAccentColors(accent);
        var isDark = mode != ThemeMode.Light;
        var window = mode switch
        {
            ThemeMode.Light    => Color.FromArgb(244, 239, 233),
            ThemeMode.Oled     => Color.FromArgb(2,   2,   2),
            ThemeMode.Midnight => Color.FromArgb(8,   10,  24),
            _                  => Color.FromArgb(23,  20,  24)
        };
        var surface = mode switch
        {
            ThemeMode.Light    => Color.FromArgb(255, 252, 248),
            ThemeMode.Oled     => Color.FromArgb(12,  10,  14),
            ThemeMode.Midnight => Color.FromArgb(13,  16,  35),
            _                  => Color.FromArgb(31,  27,  33)
        };
        var surfaceAlt = mode switch
        {
            ThemeMode.Light    => Color.FromArgb(237, 229, 220),
            ThemeMode.Oled     => Color.FromArgb(20,  18,  22),
            ThemeMode.Midnight => Color.FromArgb(20,  24,  50),
            _                  => Color.FromArgb(40,  35,  43)
        };
        var surfaceRaised = mode switch
        {
            ThemeMode.Light    => Color.FromArgb(248, 244, 238),
            ThemeMode.Oled     => Color.FromArgb(28,  25,  30),
            ThemeMode.Midnight => Color.FromArgb(28,  33,  64),
            _                  => Color.FromArgb(48,  42,  51)
        };
        var textPrimary = mode switch
        {
            ThemeMode.Light    => Color.FromArgb(31,  23,  17),
            ThemeMode.Oled     => Color.FromArgb(248, 244, 240),
            ThemeMode.Midnight => Color.FromArgb(218, 226, 255),
            _                  => Color.FromArgb(245, 238, 229)
        };
        var textSecondary = mode switch
        {
            ThemeMode.Light    => Color.FromArgb(103, 87,  74),
            ThemeMode.Oled     => Color.FromArgb(185, 170, 158),
            ThemeMode.Midnight => Color.FromArgb(140, 158, 210),
            _                  => Color.FromArgb(189, 172, 157)
        };
        var textSoft = mode switch
        {
            ThemeMode.Light    => Color.FromArgb(126, 111, 98),
            ThemeMode.Oled     => Color.FromArgb(155, 143, 132),
            ThemeMode.Midnight => Color.FromArgb(115, 133, 185),
            _                  => Color.FromArgb(166, 149, 136)
        };
        var textMuted = mode switch
        {
            ThemeMode.Light    => Color.FromArgb(151, 137, 125),
            ThemeMode.Oled     => Color.FromArgb(118, 108, 100),
            ThemeMode.Midnight => Color.FromArgb(90,  108, 162),
            _                  => Color.FromArgb(132, 120, 111)
        };
        var border = mode switch
        {
            ThemeMode.Light    => Color.FromArgb(206, 191, 177),
            ThemeMode.Oled     => Color.FromArgb(55,  45,  38),
            ThemeMode.Midnight => Color.FromArgb(52,  65,  135),
            _                  => Color.FromArgb(87,  71,  61)
        };
        var borderStrong = Blend(border, accentPrimary, isDark ? 0.18f : 0.10f);
        var accentSoft = Blend(surfaceAlt, accentPrimary, isDark ? 0.36f : 0.18f);
        var accentContrast = GetReadableTextColor(accentPrimary);
        var danger = isDark
            ? Color.FromArgb(193, 94, 82)
            : Color.FromArgb(188, 91, 79);
        var dangerText = GetReadableTextColor(danger);

        return new ThemePalette
        {
            IsDark = isDark,
            WindowBackColor = window,
            SurfaceBackColor = surface,
            SurfaceAltBackColor = surfaceAlt,
            SurfaceRaisedColor = surfaceRaised,
            TextPrimaryColor = textPrimary,
            TextSecondaryColor = textSecondary,
            TextSoftColor = textSoft,
            TextMutedColor = textMuted,
            AccentPrimaryColor = accentPrimary,
            AccentSecondaryColor = accentSecondary,
            AccentSoftColor = accentSoft,
            AccentContrastColor = accentContrast,
            DangerColor = danger,
            DangerTextColor = dangerText,
            BorderColor = border,
            BorderStrongColor = borderStrong
        };
    }

    public static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            255,
            (int)Math.Round(from.R + ((to.R - from.R) * amount)),
            (int)Math.Round(from.G + ((to.G - from.G) * amount)),
            (int)Math.Round(from.B + ((to.B - from.B) * amount)));
    }

    public static Color WithAlpha(Color color, int alpha) =>
        Color.FromArgb(Math.Clamp(alpha, 0, 255), color);

    private static (Color Primary, Color Secondary) GetAccentColors(ThemeAccent accent) =>
        accent switch
        {
            ThemeAccent.Ocean   => (Color.FromArgb(92,  163, 255), Color.FromArgb(55,  112, 214)),
            ThemeAccent.Rose    => (Color.FromArgb(231, 138, 167), Color.FromArgb(206, 95,  129)),
            ThemeAccent.Forest  => (Color.FromArgb(128, 193, 139), Color.FromArgb(67,  140, 102)),
            ThemeAccent.Violet  => (Color.FromArgb(168, 130, 242), Color.FromArgb(118, 76,  210)),
            ThemeAccent.Crimson => (Color.FromArgb(222, 82,  102), Color.FromArgb(178, 44,  70)),
            ThemeAccent.Cyan    => (Color.FromArgb(68,  202, 222), Color.FromArgb(28,  156, 182)),
            ThemeAccent.Mint    => (Color.FromArgb(98,  212, 176), Color.FromArgb(44,  170, 136)),
            ThemeAccent.Sunset  => (Color.FromArgb(252, 132, 68),  Color.FromArgb(220, 76,  44)),
            ThemeAccent.Gold    => (Color.FromArgb(232, 192, 58),  Color.FromArgb(200, 148, 24)),
            _                   => (Color.FromArgb(244, 176, 87),  Color.FromArgb(224, 114, 83))
        };

    private static Color GetReadableTextColor(Color background)
    {
        var luminance =
            ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) / 255d;
        return luminance > 0.58d
            ? Color.FromArgb(34, 24, 17)
            : Color.FromArgb(250, 246, 241);
    }
}

internal static class ThemeControlStyler
{
    public static void ApplyComboBoxTheme(ModernComboBox comboBox, ThemePalette palette)
    {
        comboBox.BackColor = palette.SurfaceAltBackColor;
        comboBox.ForeColor = palette.TextPrimaryColor;
        comboBox.SurfaceColor = palette.SurfaceAltBackColor;
        comboBox.SurfaceHoverColor = ThemePalette.Blend(palette.SurfaceAltBackColor, palette.SurfaceRaisedColor, 0.55f);
        comboBox.SurfaceOpenColor = ThemePalette.Blend(palette.SurfaceAltBackColor, palette.AccentPrimaryColor, palette.IsDark ? 0.12f : 0.08f);
        comboBox.SurfaceActiveColor = ThemePalette.Blend(palette.SurfaceAltBackColor, palette.AccentPrimaryColor, palette.IsDark ? 0.18f : 0.14f);
        comboBox.BorderColor = palette.BorderColor;
        comboBox.BorderHoverColor = ThemePalette.Blend(palette.BorderStrongColor, palette.AccentPrimaryColor, 0.35f);
        comboBox.ButtonColor = ThemePalette.Blend(palette.SurfaceRaisedColor, palette.SurfaceAltBackColor, 0.60f);
        comboBox.ButtonActiveColor = ThemePalette.Blend(palette.SurfaceRaisedColor, palette.AccentPrimaryColor, palette.IsDark ? 0.18f : 0.12f);
        comboBox.TextColor = palette.TextPrimaryColor;
        comboBox.MutedTextColor = palette.TextMutedColor;
        comboBox.CaretColor = palette.AccentPrimaryColor;
        comboBox.FlatStyle = FlatStyle.Flat;
        comboBox.Size = new Size(comboBox.Width, 36);
    }

    public static void ApplyPrimaryButtonTheme(ModernButton button, ThemePalette palette, Color accentColor)
    {
        button.IsGhost = false;
        button.AccentColor = accentColor;
        button.SurfaceColor = palette.SurfaceBackColor;
        button.DisabledTextBlendColor = palette.TextMutedColor;
        button.CornerRadius = 8f;
        button.ForeColor = palette.AccentContrastColor;
    }

    public static void ApplyGhostButtonTheme(ModernButton button, ThemePalette palette, Color accentColor)
    {
        button.IsGhost = true;
        button.AccentColor = accentColor;
        button.SurfaceColor = palette.SurfaceAltBackColor;
        button.DisabledTextBlendColor = palette.TextMutedColor;
        button.CornerRadius = 8f;
        button.ForeColor = palette.TextSecondaryColor;
    }

    public static void ApplySliderTheme(ModernSlider slider, ThemePalette palette)
    {
        slider.TrackColor = ThemePalette.WithAlpha(palette.BorderColor, palette.IsDark ? 118 : 88);
        slider.AccentStartColor = palette.AccentPrimaryColor;
        slider.AccentEndColor = palette.AccentSecondaryColor;
        slider.FocusColor = ThemePalette.WithAlpha(palette.AccentPrimaryColor, palette.IsDark ? 160 : 185);
    }

    public static void ApplyCheckBoxTheme(CheckBox checkBox, ThemePalette palette)
    {
        checkBox.BackColor = Color.Transparent;
        checkBox.FlatStyle = FlatStyle.Flat;
        checkBox.ForeColor = palette.TextSecondaryColor;
    }

    public static void ApplySwitchTheme(ModernSwitch @switch, ThemePalette palette)
    {
        @switch.OnColor = palette.AccentPrimaryColor;
        @switch.OffColor = palette.BorderColor;
        @switch.ThumbColor = palette.TextPrimaryColor;
        @switch.BackColor = palette.SurfaceBackColor;
    }
}
