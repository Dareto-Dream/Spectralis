namespace Spectralis.Core.Visualizers;

/// <summary>Port of the WinForms VisualizerTheme; same slots, toolkit-agnostic colors.</summary>
public readonly record struct VisualizerPalette(
    VizColor BackgroundTopColor,
    VizColor BackgroundBottomColor,
    VizColor AmbientGlowColor,
    VizColor AmbientGridColor,
    VizColor BarGlowColor,
    VizColor BarStartColor,
    VizColor BarEndColor,
    VizColor PeakColor,
    VizColor HudLabelColor,
    VizColor HudInfoColor,
    VizColor PlaceholderColor,
    VizColor DiskFillColor,
    VizColor DiskGrooveColor,
    VizColor HubColor,
    VizColor HubDotColor,
    VizColor RingColor,
    VizColor IdleLabelColor)
{
    public static VisualizerPalette Default =>
        new(
            VizColor.FromRgb(24, 19, 24),
            VizColor.FromRgb(10, 8, 12),
            VizColor.FromRgb(244, 152, 82),
            VizColor.FromRgb(159, 121, 88),
            VizColor.FromRgb(238, 144, 94),
            VizColor.FromRgb(248, 188, 98),
            VizColor.FromRgb(226, 111, 80),
            VizColor.FromRgb(255, 235, 189),
            VizColor.FromRgb(244, 236, 227),
            VizColor.FromRgb(191, 174, 159),
            VizColor.FromRgb(188, 175, 161),
            VizColor.FromRgb(34, 29, 35),
            VizColor.FromRgb(168, 143, 120),
            VizColor.FromRgb(20, 17, 22),
            VizColor.FromRgb(176, 132, 93),
            VizColor.FromRgb(173, 124, 90),
            VizColor.FromRgb(170, 156, 145));

    /// <summary>Derives a palette from base/raised surfaces and a single accent — the
    /// design-token path used by the Avalonia app.</summary>
    public static VisualizerPalette FromAccent(VizColor bgBase, VizColor bgRaised, VizColor accent, VizColor inkPrimary, VizColor inkSecondary, VizColor inkMuted)
    {
        var accentWarm = VizColor.Blend(accent, VizColor.FromRgb(255, 235, 189), 0.35f);
        return new VisualizerPalette(
            BackgroundTopColor: bgRaised,
            BackgroundBottomColor: bgBase,
            AmbientGlowColor: accent,
            AmbientGridColor: VizColor.Blend(inkMuted, accent, 0.25f),
            BarGlowColor: VizColor.Blend(accent, accentWarm, 0.55f),
            BarStartColor: accentWarm,
            BarEndColor: accent,
            PeakColor: VizColor.Blend(accentWarm, VizColor.FromRgb(255, 255, 255), 0.45f),
            HudLabelColor: inkPrimary,
            HudInfoColor: inkSecondary,
            PlaceholderColor: inkSecondary,
            DiskFillColor: VizColor.Blend(bgRaised, bgBase, 0.4f),
            DiskGrooveColor: VizColor.Blend(inkMuted, accent, 0.3f),
            HubColor: bgBase,
            HubDotColor: VizColor.Blend(accent, inkSecondary, 0.3f),
            RingColor: VizColor.Blend(accent, inkMuted, 0.4f),
            IdleLabelColor: inkMuted);
    }
}
