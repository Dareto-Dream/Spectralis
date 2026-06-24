using System.Drawing;

namespace Spectralis;

internal sealed class EmbeddedVisualizerHostState(EmbeddedVisualizerStyle style)
{
    private const int MaxInstructions = 1024;

    private readonly List<EmbeddedDrawInstruction> instructions = new(256);
    private Color currentColor = style.StrokeColor;
    private float currentThickness = style.StrokeThickness;
    private float[] spectrum = Array.Empty<float>();
    private float[] waveform = Array.Empty<float>();

    public float PlaybackTimeSeconds { get; private set; }

    public float PeakLevel { get; private set; }

    public float RmsLevel { get; private set; }

    public int SpectrumLength => spectrum.Length;

    public int WaveformLength => waveform.Length;

    public void BeginFrame(VisualizerScene scene)
    {
        instructions.Clear();
        currentColor = style.StrokeColor;
        currentThickness = style.StrokeThickness;
        PlaybackTimeSeconds = Math.Max(0, scene.PlaybackTimeSeconds);
        PeakLevel = scene.PeakLevel;
        RmsLevel = scene.RmsLevel;
        spectrum = scene.SpectrumLevels;
        waveform = scene.WaveformPoints;
    }

    public IReadOnlyList<EmbeddedDrawInstruction> CreateSnapshot() => instructions.ToArray();

    public void SetColor(int red, int green, int blue, int alpha)
    {
        currentColor = Color.FromArgb(
            ClampByte(alpha),
            ClampByte(red),
            ClampByte(green),
            ClampByte(blue));
    }

    public void SetThickness(float thickness) => currentThickness = Math.Clamp(thickness, 1f, 12f);

    public void AddLine(float x1, float y1, float x2, float y2)
    {
        if (instructions.Count >= MaxInstructions)
        {
            return;
        }

        instructions.Add(new EmbeddedLineInstruction(
            Math.Clamp(x1, 0, 1),
            Math.Clamp(y1, 0, 1),
            Math.Clamp(x2, 0, 1),
            Math.Clamp(y2, 0, 1),
            currentColor,
            currentThickness));
    }

    public void AddRectangle(float x, float y, float width, float height, bool filled)
    {
        if (instructions.Count >= MaxInstructions)
        {
            return;
        }

        instructions.Add(new EmbeddedRectangleInstruction(
            Math.Clamp(x, 0, 1),
            Math.Clamp(y, 0, 1),
            Math.Clamp(width, 0, 1),
            Math.Clamp(height, 0, 1),
            currentColor,
            currentThickness,
            filled));
    }

    public void AddCircle(float centerX, float centerY, float radius, bool filled)
    {
        if (instructions.Count >= MaxInstructions)
        {
            return;
        }

        instructions.Add(new EmbeddedCircleInstruction(
            Math.Clamp(centerX, 0, 1),
            Math.Clamp(centerY, 0, 1),
            Math.Clamp(radius, 0, 1),
            currentColor,
            currentThickness,
            filled));
    }

    public float GetSpectrum(int index) =>
        index >= 0 && index < spectrum.Length ? spectrum[index] : 0;

    public float GetWaveform(int index) =>
        index >= 0 && index < waveform.Length ? waveform[index] : 0;

    private static int ClampByte(int value) => Math.Clamp(value, 0, 255);
}
