using System.Drawing;

namespace Spectralis;

internal readonly record struct VisualizerTheme(
    Color BackgroundTopColor,
    Color BackgroundBottomColor,
    Color AmbientGlowColor,
    Color AmbientGridColor,
    Color BarGlowColor,
    Color BarStartColor,
    Color BarEndColor,
    Color PeakColor,
    Color HudLabelColor,
    Color HudInfoColor,
    Color PlaceholderColor,
    Color DiskFillColor,
    Color DiskGrooveColor,
    Color HubColor,
    Color HubDotColor,
    Color RingColor,
    Color IdleLabelColor)
{
    public static VisualizerTheme Default =>
        new(
            Color.FromArgb(24, 19, 24),
            Color.FromArgb(10, 8, 12),
            Color.FromArgb(244, 152, 82),
            Color.FromArgb(159, 121, 88),
            Color.FromArgb(238, 144, 94),
            Color.FromArgb(248, 188, 98),
            Color.FromArgb(226, 111, 80),
            Color.FromArgb(255, 235, 189),
            Color.FromArgb(244, 236, 227),
            Color.FromArgb(191, 174, 159),
            Color.FromArgb(188, 175, 161),
            Color.FromArgb(34, 29, 35),
            Color.FromArgb(168, 143, 120),
            Color.FromArgb(20, 17, 22),
            Color.FromArgb(176, 132, 93),
            Color.FromArgb(173, 124, 90),
            Color.FromArgb(170, 156, 145));
}

internal sealed class VisualizerScene
{
    public required Font Font { get; init; }
    public required string ModeLabel { get; init; }
    public required VisualizerTheme Theme { get; init; }
    public required float[] SpectrumLevels { get; init; }
    public required float[] PeakHoldLevels { get; init; }
    public required float[] WaveformPoints { get; init; }
    public required float PeakLevel { get; init; }
    public required float RmsLevel { get; init; }
    public required float PlaybackTimeSeconds { get; init; }
    public required bool IsActive { get; init; }
    public required bool ShowPeaks { get; init; }
    public required Image? AlbumArt { get; init; }
    public required float DiskAngle { get; init; }
    public required float AnimationPhase { get; init; }
    public required MidiNoteState[] MidiNotes { get; init; }
    public required string? MidiInstrumentName { get; init; }

    // Phase 1 additions — populated from VisualizerFrame each tick
    public float[] WaveformL { get; init; } = Array.Empty<float>();
    public float[] WaveformR { get; init; } = Array.Empty<float>();
    public float[] RawFftBins { get; init; } = Array.Empty<float>();
    public float LufsMomentary { get; init; }
    public float LufsShortTerm { get; init; }
    public float RmsFast { get; init; }
    public float RmsSlow { get; init; }
    public float StereoCorrelation { get; init; }
    public float[][] SpectrogramHistory { get; init; } = Array.Empty<float[]>();
    public int SpectrogramNewestIndex { get; init; }
}
