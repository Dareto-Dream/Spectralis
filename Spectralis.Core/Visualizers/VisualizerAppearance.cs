namespace Spectralis.Core.Visualizers;

public enum VisualizerDetail { Low, Balanced, High }
public enum VisualizerColorway { Theme, Aurora, Ember, Glacier, Monochrome }

/// <summary>Shared by the player, previews, and export. No renderer owns mutable preferences.</summary>
public sealed record VisualizerAppearance
{
    public int MotionPercent { get; init; } = 100;
    public VisualizerDetail Detail { get; init; } = VisualizerDetail.Balanced;
    public VisualizerColorway Colorway { get; init; } = VisualizerColorway.Theme;
    public bool ShowHud { get; init; } = true;
    public bool ReduceMotion { get; init; }

    public VisualizerAppearance Normalize() => this with
    {
        MotionPercent = Math.Clamp(MotionPercent, 0, 200),
        Detail = Enum.IsDefined(Detail) ? Detail : VisualizerDetail.Balanced,
        Colorway = Enum.IsDefined(Colorway) ? Colorway : VisualizerColorway.Theme,
    };

    public VisualizerPalette Apply(VisualizerPalette theme)
    {
        if (Colorway == VisualizerColorway.Theme) return theme;
        var (start, end) = Colorway switch
        {
            VisualizerColorway.Aurora => (VizColor.FromRgb(88, 244, 202), VizColor.FromRgb(154, 115, 255)),
            VisualizerColorway.Ember => (VizColor.FromRgb(255, 202, 103), VizColor.FromRgb(247, 76, 94)),
            VisualizerColorway.Glacier => (VizColor.FromRgb(173, 245, 255), VizColor.FromRgb(68, 123, 255)),
            _ => (VizColor.FromRgb(242, 244, 248), VizColor.FromRgb(125, 137, 155)),
        };
        return theme with
        {
            BarStartColor = start, BarEndColor = end, BarGlowColor = start,
            AmbientGlowColor = end, AmbientGridColor = end, RingColor = end,
            PeakColor = VizColor.Blend(start, VizColor.FromRgb(255, 255, 255), .6f),
        };
    }
}
