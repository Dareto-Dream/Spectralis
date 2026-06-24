namespace Spectralis;

internal sealed record VisualizerDefinition(
    VisualizerMode Mode,
    string Label,
    IVisualizerRenderer Renderer,
    bool RequiresAlbumArt = false,
    bool MidiOnly = false);

// Register each visualizer once here so the form, settings dialog, and control all stay in sync.
internal static class VisualizerCatalog
{
    private static readonly VisualizerDefinition[] Definitions =
    [
        new(VisualizerMode.Spectrum,       "Spectrum",        new SpectrumBarsVisualizerRenderer(mirrored: false)),
        new(VisualizerMode.MirrorSpectrum, "Mirror Spectrum", new SpectrumBarsVisualizerRenderer(mirrored: true)),
        new(VisualizerMode.Waveform,       "Waveform",        new WaveformVisualizerRenderer()),
        new(VisualizerMode.SpinningDisk,   "Spinning Disk",   new SpinningDiskVisualizerRenderer(), RequiresAlbumArt: true),
        new(VisualizerMode.AlbumCover,     "Album Cover",     new AlbumCoverVisualizerRenderer(), RequiresAlbumArt: true),
        new(VisualizerMode.RadialSpectrum, "Radial Spectrum", new RadialSpectrumVisualizerRenderer()),
        new(VisualizerMode.Oscilloscope,   "Oscilloscope",    new OscilloscopeVisualizerRenderer()),
        new(VisualizerMode.VUMeter,        "VU Meter",        new VUMeterVisualizerRenderer()),
        new(VisualizerMode.SpectrumWave,   "Spectrum Wave",   new SpectrumWaveVisualizerRenderer()),
        new(VisualizerMode.Graph3D,        "3D Graph",        new Graph3DVisualizerRenderer()),
        new(VisualizerMode.DancingColors,  "Dancing Colors",  new DancingColorsVisualizerRenderer()),
        new(VisualizerMode.Sphere3D,       "3D Sphere",       new Sphere3DVisualizerRenderer()),
        new(VisualizerMode.PianoRoll,      "MIDI Piano",      new PianoRollVisualizerRenderer(), MidiOnly: true),
        new(VisualizerMode.Spectrogram,    "Spectrogram",     new SpectrogramVisualizerRenderer()),
        new(VisualizerMode.Stereometer,    "Stereometer",     new StereometerVisualizerRenderer()),
        new(VisualizerMode.LoudnessMeter,  "Loudness Meter",  new LoudnessMeterVisualizerRenderer()),
    ];

    private static readonly Dictionary<VisualizerMode, VisualizerDefinition> DefinitionsByMode =
        Definitions.ToDictionary(static definition => definition.Mode);

    public static IReadOnlyList<VisualizerDefinition> All => Definitions;

    public static VisualizerDefinition GetDefinition(VisualizerMode mode) =>
        DefinitionsByMode.TryGetValue(mode, out var definition)
            ? definition
            : DefinitionsByMode[VisualizerMode.MirrorSpectrum];

    public static SelectionOption<VisualizerMode>[] GetOptions(bool includeAlbumArtDependent, bool isMidi = false) =>
        Definitions
            .Where(definition => (includeAlbumArtDependent || !definition.RequiresAlbumArt) && (isMidi || !definition.MidiOnly))
            .Select(static definition => new SelectionOption<VisualizerMode>(definition.Label, definition.Mode))
            .ToArray();

    public static bool IsAvailable(VisualizerMode mode, bool hasAlbumArt, bool isMidi = false)
    {
        var definition = GetDefinition(mode);
        return (!definition.RequiresAlbumArt || hasAlbumArt) && (isMidi || !definition.MidiOnly);
    }

    public static VisualizerMode GetPreferredMode(VisualizerMode preferredMode, bool hasAlbumArt, bool isMidi = false)
    {
        if (IsAvailable(preferredMode, hasAlbumArt, isMidi))
            return preferredMode;

        return IsAvailable(VisualizerMode.MirrorSpectrum, hasAlbumArt, isMidi)
            ? VisualizerMode.MirrorSpectrum
            : GetOptions(includeAlbumArtDependent: hasAlbumArt, isMidi: isMidi).First().Value;
    }
}
