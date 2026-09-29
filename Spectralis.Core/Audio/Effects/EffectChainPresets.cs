namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Built-in whole-rack presets — a starting point ("Default") and a one-click
/// "make it sound good" chain ("Make Music Peak"). User-saved chain presets are
/// stored separately by the app's <c>EffectChainPresetStore</c>, same split as
/// <see cref="EqPresets"/> vs <c>EqPresetStore</c>.
/// </summary>
public static class EffectChainPresets
{
    public const string DefaultName = "Default";
    public const string MakeMusicPeakName = "Make Music Peak";

    public static IReadOnlyList<string> BuiltInNames { get; } = [DefaultName, MakeMusicPeakName];

    public static IReadOnlyList<IAudioEffect> Build(string name) => name switch
    {
        DefaultName => BuildDefault(),
        MakeMusicPeakName => BuildMakeMusicPeak(),
        _ => throw new ArgumentException($"Unknown built-in chain preset: {name}"),
    };

    /// <summary>Nothing but a flat parametric EQ — a clean slate to build a rack from.</summary>
    private static IReadOnlyList<IAudioEffect> BuildDefault() => [new ParametricEqEffect()];

    /// <summary>
    /// Bass-boosted, glued, and widened, with a slow auto-pan sweep and a touch of
    /// tape warmth for movement/ear candy, capped by a limiter so it stays safe to
    /// leave running on anything. Ordered EQ → dynamics → coloring → space → ceiling.
    /// </summary>
    private static IReadOnlyList<IAudioEffect> BuildMakeMusicPeak()
    {
        var eq = new ParametricEqEffect();
        eq.LoadPreset(new EqPreset(
            MakeMusicPeakName,
            0f,
            0f,
            [
                new EqBand(31, 5.5f, 1.1f, EqFilterType.Peak),
                new EqBand(62, 4.5f, 1.1f, EqFilterType.Peak),
                new EqBand(125, 2f, 1.0f, EqFilterType.Peak),
                new EqBand(250, 0f, 1.0f, EqFilterType.Peak),
                new EqBand(500, -1f, 1.0f, EqFilterType.Peak),
                new EqBand(1000, 0f, 1.0f, EqFilterType.Peak),
                new EqBand(2000, 1f, 1.0f, EqFilterType.Peak),
                new EqBand(4000, 1.5f, 1.0f, EqFilterType.Peak),
                new EqBand(8000, 2f, 1.0f, EqFilterType.Peak),
                new EqBand(16000, 2.5f, 1.0f, EqFilterType.Peak),
            ]));

        var compressor = new CompressorEffect();
        compressor.Parameters.Set("threshold", -18f);
        compressor.Parameters.Set("ratio", 2.5f);
        compressor.Parameters.Set("attack", 15f);
        compressor.Parameters.Set("release", 120f);
        compressor.Parameters.Set("makeup", 2f);

        var saturation = new SaturationEffect();
        saturation.Parameters.Set("drive", 2.5f);
        saturation.Parameters.Set("tone", 0.65f);
        saturation.Parameters.Set("mix", 0.3f);

        var widener = new StereoWidenerEffect();
        widener.Parameters.Set("width", 1.5f);

        var panner = new StereoPannerEffect();
        panner.AutoPanEnabled = true;
        panner.Bars = 4;
        panner.Bpm = 120;
        panner.Parameters.Set("spread", 0.35f);
        panner.WritePoints(
        [
            new PanPoint(0f, 0f),
            new PanPoint(0.25f, 0.35f),
            new PanPoint(0.5f, 0f),
            new PanPoint(0.75f, -0.35f),
        ]);

        var limiter = new LimiterEffect();
        limiter.Parameters.Set("ceiling", -0.5f);
        limiter.Parameters.Set("release", 60f);

        return [eq, compressor, saturation, widener, panner, limiter];
    }
}
