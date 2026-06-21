using Spectralis.Core.Common;

namespace Spectralis.Core.Visualizers;

public sealed record VisualizerFrame(
    float[] Spectrum,
    float[] Waveform,
    float PeakLevel,
    float RmsLevel,
    MidiNoteState[]? MidiNotes = null,
    string? MidiInstrumentName = null)
{
    public static VisualizerFrame Empty { get; } = new(Array.Empty<float>(), Array.Empty<float>(), 0, 0);

    public MidiNoteState[] ActiveMidiNotes => MidiNotes ?? Array.Empty<MidiNoteState>();

    // Waveform is kept as an alias for WaveformL
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
