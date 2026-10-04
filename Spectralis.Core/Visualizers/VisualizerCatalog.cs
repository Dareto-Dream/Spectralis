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

        // GPU visualizers. The index is the built-in's position in wgpu-host's viz.rs BUILTINS.
        // Each carries a CPU look-alike for machines (or moments) where the GPU can't draw a frame.
        new(VisualizerMode.GpuBars, "GPU Bars", new GpuVisualizerRenderer(0, new SpectrumBarsRenderer(mirrored: false)), RequiresGpu: true),
        new(VisualizerMode.GpuRadial, "GPU Radial", new GpuVisualizerRenderer(1, new RadialSpectrumRenderer()), RequiresGpu: true),
        new(VisualizerMode.GpuTunnel, "GPU Tunnel", new GpuVisualizerRenderer(2, new SpectrumWaveRenderer()), RequiresGpu: true),
    ];

    private static readonly Dictionary<VisualizerMode, VisualizerDefinition> DefinitionsByMode =
        Definitions.ToDictionary(static definition => definition.Mode);

    /// <summary>Every visualizer that can run right now: GPU ones only while a GPU frame source is available.</summary>
    public static IReadOnlyList<VisualizerDefinition> All =>
        GpuVisualizers.IsAvailable ? Definitions : Definitions.Where(static d => !d.RequiresGpu).ToArray();

    /// <summary>Everything registered, available or not. <see cref="GetDefinition"/> resolves from this, so a saved GPU mode still renders (via its CPU fallback) where the GPU is missing.</summary>
    public static IReadOnlyList<VisualizerDefinition> AllRegistered => Definitions;

    public static VisualizerDefinition GetDefinition(VisualizerMode mode) =>
        DefinitionsByMode.TryGetValue(mode, out var definition)
            ? definition
            : DefinitionsByMode[VisualizerMode.MirrorSpectrum];

    public static bool IsAvailable(VisualizerMode mode, bool hasAlbumArt) =>
        !GetDefinition(mode).RequiresAlbumArt || hasAlbumArt;

    public static VisualizerMode GetPreferredMode(VisualizerMode preferredMode, bool hasAlbumArt) =>
        IsAvailable(preferredMode, hasAlbumArt) ? preferredMode : VisualizerMode.MirrorSpectrum;
}
