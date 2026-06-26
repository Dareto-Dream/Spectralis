using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Spectralis;

public sealed class SpectrumVisualizerControl : Control
{
    private static readonly EmbeddedVisualizerRenderer embeddedVisualizerRenderer = new();
    private readonly float[] spectrumLevels = new float[64];
    private readonly float[] peakHoldLevels = new float[64];
    private readonly float[] waveformPoints = new float[256];

    private VisualizerMode mode = VisualizerMode.MirrorSpectrum;
    private VisualizerTheme visualizerTheme = VisualizerTheme.Default;
    private bool showPeaks = true;
    private float sensitivity = 1;
    private float peakLevel;
    private float rmsLevel;
    private bool isActive;
    private Image? albumArt;
    private float diskAngle;
    private float animationPhase;
    private float playbackTimeSeconds;
    private MidiNoteState[] midiNotes = [];
    private string? midiInstrumentName;
    private EmbeddedVisualizerContext? embeddedVisualizerSource;
    private EmbeddedVisualizerSession? embeddedVisualizer;
    private EmbeddedVisualizerContext? installedVisualizerSource;
    private EmbeddedVisualizerSession? installedVisualizer;
    private ScriptVisualizerRenderer? scriptedRenderer;

    // Phase 1 fields — updated from frame each tick
    private float[] waveformL = [];
    private float[] waveformR = [];
    private float[] rawFftBins = [];
    private float lufsMomentary;
    private float lufsShortTerm;
    private float rmsFast;
    private float rmsSlow;
    private float stereoCorrelation;
    private float[][] spectrogramHistory = [];
    private int spectrogramNewestIndex;

    public SpectrumVisualizerControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = visualizerTheme.BackgroundTopColor;
        ForeColor = visualizerTheme.BarStartColor;
    }

    internal void ApplyTheme(ThemePalette palette)
    {
        visualizerTheme = new VisualizerTheme(
            ThemePalette.Blend(palette.SurfaceBackColor, palette.WindowBackColor, palette.IsDark ? 0.30f : 0.08f),
            ThemePalette.Blend(palette.WindowBackColor, Color.Black, palette.IsDark ? 0.52f : 0.06f),
            palette.AccentPrimaryColor,
            ThemePalette.Blend(palette.BorderStrongColor, palette.AccentPrimaryColor, 0.22f),
            ThemePalette.Blend(palette.AccentPrimaryColor, palette.AccentSecondaryColor, 0.55f),
            ThemePalette.Blend(palette.AccentPrimaryColor, Color.White, palette.IsDark ? 0.10f : 0.04f),
            palette.AccentSecondaryColor,
            ThemePalette.Blend(palette.TextPrimaryColor, palette.AccentPrimaryColor, palette.IsDark ? 0.16f : 0.08f),
            palette.TextPrimaryColor,
            palette.TextSecondaryColor,
            palette.TextSoftColor,
            ThemePalette.Blend(palette.SurfaceRaisedColor, palette.WindowBackColor, palette.IsDark ? 0.36f : 0.08f),
            ThemePalette.Blend(palette.TextSoftColor, palette.AccentPrimaryColor, 0.20f),
            ThemePalette.Blend(palette.WindowBackColor, Color.Black, palette.IsDark ? 0.18f : 0.02f),
            palette.AccentSecondaryColor,
            ThemePalette.Blend(palette.BorderStrongColor, palette.AccentPrimaryColor, 0.30f),
            palette.TextMutedColor);

        BackColor = visualizerTheme.BackgroundTopColor;
        ForeColor = visualizerTheme.BarStartColor;
        Invalidate();
    }

    [DefaultValue(VisualizerMode.MirrorSpectrum)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public VisualizerMode Mode
    {
        get => mode;
        set
        {
            mode = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    internal VisualizerTheme CurrentTheme => visualizerTheme;

    [DefaultValue(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool ShowPeaks
    {
        get => showPeaks;
        set
        {
            showPeaks = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Image? AlbumArt
    {
        get => albumArt;
        set
        {
            albumArt = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public EmbeddedVisualizerContext? EmbeddedVisualizer
    {
        set
        {
            if (ReferenceEquals(embeddedVisualizerSource, value))
                return;

            embeddedVisualizerSource = value;
            embeddedVisualizer?.Dispose();
            embeddedVisualizer = EmbeddedVisualizerSession.TryCreate(value);
            Invalidate();
        }
    }

    [Browsable(false)]
    public bool UsesEmbeddedVisualizer => embeddedVisualizer is { IsFaulted: false };

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public EmbeddedVisualizerContext? InstalledVisualizer
    {
        set
        {
            if (ReferenceEquals(installedVisualizerSource, value))
                return;

            installedVisualizerSource = value;
            installedVisualizer?.Dispose();
            installedVisualizer = EmbeddedVisualizerSession.TryCreate(value);
            Invalidate();
        }
    }

    [Browsable(false)]
    public bool UsesInstalledVisualizer => installedVisualizer is { IsFaulted: false };

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal ScriptVisualizerRenderer? ScriptedRenderer
    {
        get => scriptedRenderer;
        set
        {
            scriptedRenderer = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    public string? InstalledVisualizerDisplayName => installedVisualizerSource?.DisplayName;

    [DefaultValue(1f)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public float Sensitivity
    {
        get => sensitivity;
        set
        {
            sensitivity = Math.Clamp(value, 0.4f, 2.5f);
            Invalidate();
        }
    }

    public void UpdateFrame(VisualizerFrame frame, bool activePlayback, float playbackSeconds)
    {
        isActive = activePlayback;
        playbackTimeSeconds = Math.Max(0, playbackSeconds);

        for (var index = 0; index < spectrumLevels.Length; index++)
        {
            var incoming = index < frame.Spectrum.Length ? Math.Clamp(frame.Spectrum[index] * sensitivity, 0, 1.25f) : 0;
            spectrumLevels[index] = Math.Max(incoming, spectrumLevels[index] * (activePlayback ? 0.80f : 0.90f));
            peakHoldLevels[index] = showPeaks
                ? Math.Max(spectrumLevels[index], peakHoldLevels[index] - 0.03f)
                : 0;
        }

        for (var index = 0; index < waveformPoints.Length; index++)
        {
            var incoming = index < frame.Waveform.Length ? frame.Waveform[index] * sensitivity : 0;
            waveformPoints[index] = (waveformPoints[index] * 0.35f) + (incoming * 0.65f);
        }

        peakLevel = Math.Max(frame.PeakLevel * sensitivity, peakLevel * (activePlayback ? 0.90f : 0.95f));
        rmsLevel = Math.Max(frame.RmsLevel * sensitivity, rmsLevel * (activePlayback ? 0.92f : 0.96f));
        midiNotes = frame.ActiveMidiNotes;
        midiInstrumentName = frame.MidiInstrumentName;

        waveformL = frame.WaveformL.Length > 0 ? frame.WaveformL : waveformL;
        waveformR = frame.WaveformR.Length > 0 ? frame.WaveformR : waveformR;
        rawFftBins = frame.RawFftBins.Length > 0 ? frame.RawFftBins : rawFftBins;
        lufsMomentary = frame.LufsMomentary;
        lufsShortTerm = frame.LufsShortTerm;
        rmsFast = frame.RmsFast;
        rmsSlow = frame.RmsSlow;
        stereoCorrelation = frame.StereoCorrelation;
        if (frame.SpectrogramHistory.Length > 0)
        {
            spectrogramHistory = frame.SpectrogramHistory;
            spectrogramNewestIndex = frame.SpectrogramNewestIndex;
        }

        if (activePlayback)
        {
            if (mode == VisualizerMode.SpinningDisk)
                diskAngle = (diskAngle + 0.38f) % 360f;

            var phaseStep = mode switch
            {
                VisualizerMode.RadialSpectrum => 0.85f,
                VisualizerMode.Graph3D => 1.05f,
                VisualizerMode.DancingColors => 1.65f,
                VisualizerMode.Sphere3D => 0.90f,
                VisualizerMode.PianoRoll => 0.70f,
                _ => 0f
            };

            if (phaseStep > 0)
                animationPhase = (animationPhase + phaseStep + (frame.RmsLevel * 2.4f)) % 360f;
        }

        Invalidate();
    }

    public void ClearFrame()
    {
        Array.Clear(spectrumLevels);
        Array.Clear(peakHoldLevels);
        Array.Clear(waveformPoints);
        peakLevel = 0;
        rmsLevel = 0;
        isActive = false;
        diskAngle = 0;
        animationPhase = 0;
        playbackTimeSeconds = 0;
        midiNotes = [];
        midiInstrumentName = null;
        waveformL = [];
        waveformR = [];
        rawFftBins = [];
        lufsMomentary = 0;
        lufsShortTerm = 0;
        rmsFast = 0;
        rmsSlow = 0;
        stereoCorrelation = 0;
        spectrogramHistory = [];
        spectrogramNewestIndex = 0;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (scriptedRenderer is not null)
        {
            scriptedRenderer.Draw(e.Graphics, bounds, CreateScene("Script"));
            return;
        }

        if (embeddedVisualizer is { IsFaulted: false } session)
        {
            var embeddedScene = CreateScene(session.DisplayLabel);
            var instructions = session.Render(embeddedScene);
            if (!session.IsFaulted)
            {
                embeddedVisualizerRenderer.Draw(e.Graphics, bounds, embeddedScene, instructions);
                return;
            }
        }

        if (installedVisualizer is { IsFaulted: false } installedSession)
        {
            var installedScene = CreateScene(installedSession.DisplayLabel);
            var instructions = installedSession.Render(installedScene);
            if (!installedSession.IsFaulted)
            {
                embeddedVisualizerRenderer.Draw(e.Graphics, bounds, installedScene, instructions);
                return;
            }
        }

        var definition = VisualizerCatalog.GetDefinition(mode);
        definition.Renderer.Draw(e.Graphics, bounds, CreateScene(definition.Label));
    }

    private VisualizerScene CreateScene(string modeLabel) =>
        new()
        {
            Font = Font,
            ModeLabel = modeLabel,
            Theme = visualizerTheme,
            SpectrumLevels = spectrumLevels,
            PeakHoldLevels = peakHoldLevels,
            WaveformPoints = waveformPoints,
            PeakLevel = peakLevel,
            RmsLevel = rmsLevel,
            PlaybackTimeSeconds = playbackTimeSeconds,
            IsActive = isActive,
            ShowPeaks = showPeaks,
            AlbumArt = albumArt,
            DiskAngle = diskAngle,
            AnimationPhase = animationPhase,
            MidiNotes = midiNotes,
            MidiInstrumentName = midiInstrumentName,
            WaveformL = waveformL,
            WaveformR = waveformR,
            RawFftBins = rawFftBins,
            LufsMomentary = lufsMomentary,
            LufsShortTerm = lufsShortTerm,
            RmsFast = rmsFast,
            RmsSlow = rmsSlow,
            StereoCorrelation = stereoCorrelation,
            SpectrogramHistory = spectrogramHistory,
            SpectrogramNewestIndex = spectrogramNewestIndex,
        };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            embeddedVisualizerSource = null;
            embeddedVisualizer?.Dispose();
            embeddedVisualizer = null;
            installedVisualizerSource = null;
            installedVisualizer?.Dispose();
            installedVisualizer = null;
        }

        base.Dispose(disposing);
    }
}
