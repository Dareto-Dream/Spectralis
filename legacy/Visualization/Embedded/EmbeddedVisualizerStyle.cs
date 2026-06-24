using System.Drawing;

namespace Spectralis;

internal readonly record struct EmbeddedVisualizerStyle(
    Color StrokeColor,
    float StrokeThickness,
    float Amplitude,
    int SampleCount)
{
    public static EmbeddedVisualizerStyle FromContext(EmbeddedVisualizerContext context)
    {
        var config = context.GetDataByBinding("config");
        var strokeColor = TryReadColor(config?.TryGetString("color", "strokeColor", "lineColor")) ??
            Color.FromArgb(255, 0, 255, 170);
        var strokeThickness = config is not null && config.TryGetNumber("thickness", out var thickness)
            ? Math.Clamp(thickness, 1f, 10f)
            : 2.2f;
        var amplitude = config is not null && config.TryGetNumber("amplitude", out var amplitudeValue)
            ? Math.Clamp(amplitudeValue, 5f, 100f)
            : 68f;
        var sampleCount = config is not null && config.TryGetNumber("sampleCount", out var sampleCountValue)
            ? Math.Clamp((int)Math.Round(sampleCountValue), 24, 256)
            : 96;

        return new EmbeddedVisualizerStyle(strokeColor, strokeThickness, amplitude, sampleCount);
    }

    private static Color? TryReadColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return ColorTranslator.FromHtml(value.Trim());
        }
        catch
        {
            return null;
        }
    }
}
