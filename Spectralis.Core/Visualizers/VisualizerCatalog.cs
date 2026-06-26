using Spectralis.Core.Visualizers.Renderers;

namespace Spectralis.Core.Visualizers;

public sealed record VisualizerDefinition(
    VisualizerMode Mode,
    string Label,
    IVisualizerRenderer Renderer,
    bool RequiresAlbumArt = false,
    bool RequiresMidi = false);

// Register each visualizer once here so the picker, settings, and host control stay in sync.
public static class VisualizerCatalog
{
    private static readonly VisualizerDefinition[] Definitions =
    [
        new(VisualizerMode.Spectrum, "Spectrum", new SpectrumBarsRenderer(mirrored: false)),
        new(VisualizerMode.MirrorSpectrum, "Mirror Spectrum", new SpectrumBarsRenderer(mirrored: true)),
        new(VisualizerMode.Waveform, "Waveform", new WaveformRenderer()),
        new(VisualizerMode.SpinningDisk, "Spinning Disk", new SpinningDiskRenderer(), RequiresAlbumArt: true),
        new(VisualizerMode.RadialSpectrum, "Radial Spectrum", new RadialSpectrumRenderer()),
        new(VisualizerMode.Oscilloscope, "Oscilloscope", new OscilloscopeRenderer()),
        new(VisualizerMode.VUMeter, "VU Meter", new VUMeterRenderer()),
        new(VisualizerMode.SpectrumWave, "Spectrum Wave", new SpectrumWaveRenderer()),
        new(VisualizerMode.Graph3D, "3D Graph", new Graph3DRenderer()),
        new(VisualizerMode.DancingColors, "Dancing Colors", new DancingColorsRenderer()),
        new(VisualizerMode.Sphere3D, "3D Sphere", new Sphere3DRenderer()),
        new(VisualizerMode.AlbumCover, "Album Cover", new AlbumCoverRenderer(), RequiresAlbumArt: true),
        new(VisualizerMode.PianoRoll, "Piano Roll", new PianoRollRenderer(), RequiresMidi: true),
        new(VisualizerMode.Spectrogram, "Spectrogram", new SpectrogramRenderer()),
        new(VisualizerMode.Stereometer, "Stereometer", new StereometerRenderer()),
        new(VisualizerMode.LoudnessMeter, "Loudness Meter", new LoudnessMeterRenderer()),
    ];

    private static readonly Dictionary<VisualizerMode, VisualizerDefinition> DefinitionsByMode =
        Definitions.ToDictionary(static definition => definition.Mode);

    public static IReadOnlyList<VisualizerDefinition> All => Definitions;

    public static VisualizerDefinition GetDefinition(VisualizerMode mode) =>
        DefinitionsByMode.TryGetValue(mode, out var definition)
            ? definition
            : DefinitionsByMode[VisualizerMode.MirrorSpectrum];

    public static bool IsAvailable(VisualizerMode mode, bool hasAlbumArt) =>
        !GetDefinition(mode).RequiresAlbumArt || hasAlbumArt;

    public static VisualizerMode GetPreferredMode(VisualizerMode preferredMode, bool hasAlbumArt) =>
        IsAvailable(preferredMode, hasAlbumArt) ? preferredMode : VisualizerMode.MirrorSpectrum;
}
