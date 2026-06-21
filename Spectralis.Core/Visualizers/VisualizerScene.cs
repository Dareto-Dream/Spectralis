using Spectralis.Core.Common;

namespace Spectralis.Core.Visualizers;

public enum VisualizerMode
{
    Spectrum,
    MirrorSpectrum,
    Waveform,
    SpinningDisk,
    RadialSpectrum,
    Oscilloscope,
    VUMeter,
    SpectrumWave,
    Graph3D,
    DancingColors,
    Sphere3D,
    AlbumCover,
    PianoRoll,
    Spectrogram,
    Stereometer,
    LoudnessMeter,

    // Legacy enum stubs carried over for settings compatibility; no renderers yet
    // (they were unimplemented in the WinForms app as well).
    LedMeter,
    Vectorscope,
    BounceBars,
    CircularEq,
    BlockGrid,
}

/// <summary>Immutable per-frame scene handed to renderers.</summary>
public sealed class VisualizerScene
{
    public required string ModeLabel { get; init; }
    public required VisualizerPalette Theme { get; init; }
    public required float[] SpectrumLevels { get; init; }
    public required float[] PeakHoldLevels { get; init; }
    public required float[] WaveformPoints { get; init; }
    public required float PeakLevel { get; init; }
    public required float RmsLevel { get; init; }
    public required float PlaybackTimeSeconds { get; init; }
    public required bool IsActive { get; init; }
    public required bool ShowPeaks { get; init; }
    public required IVizImage? AlbumArt { get; init; }
    public required float DiskAngle { get; init; }
    public required float AnimationPhase { get; init; }
    public required MidiNoteState[] MidiNotes { get; init; }
    public required string? MidiInstrumentName { get; init; }

    // Extended audio analysis channels used by the meter/scope renderers.
    public float[] WaveformLeft { get; init; } = Array.Empty<float>();
    public float[] WaveformRight { get; init; } = Array.Empty<float>();
    public float LufsMomentary { get; init; }
    public float LufsShortTerm { get; init; }
    public float RmsFast { get; init; }
    public float RmsSlow { get; init; }
    public float StereoCorrelation { get; init; }
    public float[][] SpectrogramHistory { get; init; } = Array.Empty<float[]>();
    public int SpectrogramNewestIndex { get; init; }
}

/// <summary>
/// Frame smoothing and animation state — ported verbatim from the WinForms
/// SpectrumVisualizerControl.UpdateFrame: spectrum decay, peak hold, waveform
/// low-pass, disk angle, per-mode animation phase.
/// </summary>
public sealed class VisualizerSceneState
{
    private readonly float[] _spectrumLevels = new float[64];
    private readonly float[] _peakHoldLevels = new float[64];
    private readonly float[] _waveformPoints = new float[256];

    private float _peakLevel;
    private float _rmsLevel;
    private bool _isActive;
    private float _diskAngle;
    private float _animationPhase;
    private float _playbackTimeSeconds;
    private MidiNoteState[] _midiNotes = Array.Empty<MidiNoteState>();
    private string? _midiInstrumentName;
    private float[] _waveformLeft = Array.Empty<float>();
    private float[] _waveformRight = Array.Empty<float>();
    private float _lufsMomentary;
    private float _lufsShortTerm;
    private float _rmsFast;
    private float _rmsSlow;
    private float _stereoCorrelation;
    private float[][] _spectrogramHistory = Array.Empty<float[]>();
    private int _spectrogramNewestIndex;

    public bool ShowPeaks { get; set; } = true;

    private float _sensitivity = 1f;

    public float Sensitivity
    {
        get => _sensitivity;
        set => _sensitivity = Math.Clamp(value, 0.4f, 2.5f);
    }

    public VisualizerPalette Palette { get; set; } = VisualizerPalette.Default;

    public IVizImage? AlbumArt { get; set; }

    public float DiskAngle => _diskAngle;
    public float AnimationPhase => _animationPhase;

    public void UpdateFrame(VisualizerFrame frame, bool activePlayback, float playbackSeconds, VisualizerMode mode)
    {
        _isActive = activePlayback;
        _playbackTimeSeconds = Math.Max(0, playbackSeconds);

        for (var index = 0; index < _spectrumLevels.Length; index++)
        {
            var incoming = index < frame.Spectrum.Length
                ? Math.Clamp(frame.Spectrum[index] * _sensitivity, 0, 1.25f)
                : 0;
            _spectrumLevels[index] = Math.Max(incoming, _spectrumLevels[index] * (activePlayback ? 0.80f : 0.90f));
            _peakHoldLevels[index] = ShowPeaks
                ? Math.Max(_spectrumLevels[index], _peakHoldLevels[index] - 0.03f)
                : 0;
        }

        for (var index = 0; index < _waveformPoints.Length; index++)
        {
            var incoming = index < frame.Waveform.Length ? frame.Waveform[index] * _sensitivity : 0;
            _waveformPoints[index] = (_waveformPoints[index] * 0.35f) + (incoming * 0.65f);
        }

        _peakLevel = Math.Max(frame.PeakLevel * _sensitivity, _peakLevel * (activePlayback ? 0.90f : 0.95f));
        _rmsLevel = Math.Max(frame.RmsLevel * _sensitivity, _rmsLevel * (activePlayback ? 0.92f : 0.96f));
        _midiNotes = frame.ActiveMidiNotes;
        _midiInstrumentName = frame.MidiInstrumentName;
        _waveformLeft = frame.WaveformL.Length > 0 ? frame.WaveformL : frame.Waveform;
        _waveformRight = frame.WaveformR;
        _lufsMomentary = frame.LufsMomentary;
        _lufsShortTerm = frame.LufsShortTerm;
        _rmsFast = frame.RmsFast;
        _rmsSlow = frame.RmsSlow;
        _stereoCorrelation = frame.StereoCorrelation;
        _spectrogramHistory = frame.SpectrogramHistory;
        _spectrogramNewestIndex = frame.SpectrogramNewestIndex;

        if (activePlayback)
        {
            if (mode == VisualizerMode.SpinningDisk)
            {
                _diskAngle = (_diskAngle + 0.38f) % 360f;
            }

            var phaseStep = mode switch
            {
                VisualizerMode.RadialSpectrum => 0.85f,
                VisualizerMode.Graph3D => 1.05f,
                VisualizerMode.DancingColors => 1.65f,
                VisualizerMode.Sphere3D => 0.90f,
                _ => 0f,
            };

            if (phaseStep > 0)
            {
                _animationPhase = (_animationPhase + phaseStep + (frame.RmsLevel * 2.4f)) % 360f;
            }
        }
    }

    public void Clear()
    {
        Array.Clear(_spectrumLevels);
        Array.Clear(_peakHoldLevels);
        Array.Clear(_waveformPoints);
        _peakLevel = 0;
        _rmsLevel = 0;
        _isActive = false;
        _diskAngle = 0;
        _animationPhase = 0;
        _playbackTimeSeconds = 0;
        _midiNotes = Array.Empty<MidiNoteState>();
        _midiInstrumentName = null;
        _waveformLeft = Array.Empty<float>();
        _waveformRight = Array.Empty<float>();
        _lufsMomentary = 0;
        _lufsShortTerm = 0;
        _rmsFast = 0;
        _rmsSlow = 0;
        _stereoCorrelation = 0;
        _spectrogramHistory = Array.Empty<float[]>();
        _spectrogramNewestIndex = 0;
    }

    public VisualizerScene CreateScene(string modeLabel) =>
        new()
        {
            ModeLabel = modeLabel,
            Theme = Palette,
            SpectrumLevels = _spectrumLevels,
            PeakHoldLevels = _peakHoldLevels,
            WaveformPoints = _waveformPoints,
            PeakLevel = _peakLevel,
            RmsLevel = _rmsLevel,
            PlaybackTimeSeconds = _playbackTimeSeconds,
            IsActive = _isActive,
            ShowPeaks = ShowPeaks,
            AlbumArt = AlbumArt,
            DiskAngle = _diskAngle,
            AnimationPhase = _animationPhase,
            MidiNotes = _midiNotes,
            MidiInstrumentName = _midiInstrumentName,
            WaveformLeft = _waveformLeft,
            WaveformRight = _waveformRight,
            LufsMomentary = _lufsMomentary,
            LufsShortTerm = _lufsShortTerm,
            RmsFast = _rmsFast,
            RmsSlow = _rmsSlow,
            StereoCorrelation = _stereoCorrelation,
            SpectrogramHistory = _spectrogramHistory,
            SpectrogramNewestIndex = _spectrogramNewestIndex,
        };
}
