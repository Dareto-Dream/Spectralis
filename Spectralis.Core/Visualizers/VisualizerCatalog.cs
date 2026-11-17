using Spectralis.Core.Visualizers.Renderers;

namespace Spectralis.Core.Visualizers;

public sealed record VisualizerDefinition(
    VisualizerMode Mode,
    string Label,
    IVisualizerRenderer Renderer,
    bool RequiresAlbumArt = false,
    bool RequiresMidi = false,
    bool RequiresGpu = false);

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

        new(VisualizerMode.Wormhole, "Wormhole", new WormholeRenderer()),
        new(VisualizerMode.Starfield, "Starfield", new StarfieldRenderer()),
        new(VisualizerMode.Orbital, "Orbital", new OrbitalRenderer()),
        new(VisualizerMode.Silk, "Silk", new SilkRenderer()),
    ];

    private static readonly Dictionary<VisualizerMode, VisualizerDefinition> DefinitionsByMode =
        Definitions.ToDictionary(static definition => definition.Mode);

    public static IReadOnlyList<VisualizerDefinition> All => Definitions;
    public static IReadOnlyList<VisualizerDefinition> AllRegistered => Definitions;

    public static VisualizerMode NormalizeMode(VisualizerMode mode) => mode switch
    {
        VisualizerMode.GpuBars => VisualizerMode.Spectrum,
        VisualizerMode.GpuRadial => VisualizerMode.RadialSpectrum,
        VisualizerMode.GpuTunnel => VisualizerMode.Wormhole,
        _ => DefinitionsByMode.ContainsKey(mode) ? mode : VisualizerMode.MirrorSpectrum,
    };

    public static VisualizerDefinition GetDefinition(VisualizerMode mode) =>
        DefinitionsByMode.TryGetValue(NormalizeMode(mode), out var definition)
            ? definition
            : DefinitionsByMode[VisualizerMode.MirrorSpectrum];

    // History and scratch buffers belong to a surface, never to the process-wide catalog.
    public static IVisualizerRenderer CreateRenderer(VisualizerMode mode) => NormalizeMode(mode) switch
    {
        VisualizerMode.Spectrum => new SpectrumBarsRenderer(mirrored: false),
        VisualizerMode.MirrorSpectrum => new SpectrumBarsRenderer(mirrored: true),
        VisualizerMode.Waveform => new WaveformRenderer(),
        VisualizerMode.Spectrogram => new SpectrogramRenderer(),
        _ => GetDefinition(mode).Renderer,
    };

    public static bool IsAvailable(VisualizerMode mode, bool hasAlbumArt) =>
        !GetDefinition(mode).RequiresAlbumArt || hasAlbumArt;

    public static VisualizerMode GetPreferredMode(VisualizerMode preferredMode, bool hasAlbumArt) =>
        IsAvailable(preferredMode, hasAlbumArt) ? NormalizeMode(preferredMode) : VisualizerMode.MirrorSpectrum;
}
