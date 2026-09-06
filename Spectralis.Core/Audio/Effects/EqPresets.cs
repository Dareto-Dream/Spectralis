namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Built-in parametric-EQ presets. Each is a 10-band graphic curve over the ISO
/// centre frequencies (<see cref="ParametricEqEffect.DefaultFrequencies"/>) so
/// applying one keeps a predictable set of nodes on the editor. User presets are
/// stored separately by the app's <c>EqPresetStore</c>.
/// </summary>
public static class EqPresets
{
    public const string FlatName = "Flat";

    public static IReadOnlyList<EqPreset> BuiltIn { get; } = Build();

    public static EqPreset Flat => BuiltIn[0];

    private static EqPreset Graphic(string name, params (float Freq, float Gain)[] points)
    {
        var map = new Dictionary<float, float>();
        foreach (var (freq, gain) in points)
        {
            map[freq] = gain;
        }

        var bands = ParametricEqEffect.DefaultFrequencies
            .Select(f => new EqBand(f, map.TryGetValue(f, out var g) ? g : 0f, 1.0f, EqFilterType.Peak))
            .ToArray();

        return new EqPreset(name, 0f, 0f, bands);
    }

    private static IReadOnlyList<EqPreset> Build() =>
    [
        Graphic(FlatName),
        Graphic("Bass Boost", (31, 6), (62, 5), (125, 3), (250, 1)),
        Graphic("Bass Cut", (31, -6), (62, -5), (125, -3), (250, -1)),
        Graphic("Treble Boost", (2000, 1), (4000, 3), (8000, 4), (16000, 5)),
        Graphic("Vocal Clarity", (62, -2), (125, -1), (250, 1), (1000, 2), (2000, 4), (4000, 3), (8000, 1)),
        Graphic("Loudness", (31, 6), (62, 4), (125, 2), (250, 1), (4000, 2), (8000, 4), (16000, 5)),
        Graphic("Rock", (31, 4), (62, 3), (125, 1), (250, -1), (500, -1), (1000, 1), (2000, 2), (4000, 3), (8000, 3), (16000, 3)),
        Graphic("Pop", (31, -1), (125, 2), (250, 3), (500, 2), (2000, -1), (4000, 1), (8000, 2), (16000, 1)),
        Graphic("Jazz", (31, 3), (62, 2), (250, 1), (500, -1), (1000, -1), (4000, 1), (8000, 2), (16000, 3)),
        Graphic("Classical", (31, 3), (62, 2), (125, 1), (4000, 1), (8000, 2), (16000, 3)),
        Graphic("Electronic", (31, 5), (62, 4), (125, 1), (500, -1), (2000, 1), (4000, 2), (8000, 4), (16000, 5)),
        Graphic("Hip-Hop", (31, 5), (62, 4), (125, 2), (250, 1), (2000, 1), (4000, 2), (8000, 2)),
        Graphic("Podcast / Speech", (31, -8), (62, -4), (125, -1), (250, 1), (1000, 2), (2000, 3), (4000, 3), (8000, 1), (16000, -2)),
        Graphic("Lo-Fi", (31, 2), (62, 1), (2000, -2), (4000, -5), (8000, -9), (16000, -12)),
        Graphic("Warm", (31, 3), (62, 2), (125, 1), (2000, -1), (4000, -2), (8000, -3), (16000, -3)),
        Graphic("Bright", (31, -2), (62, -1), (2000, 2), (4000, 3), (8000, 4), (16000, 4)),
        Graphic("Small Speakers", (31, -8), (62, -4), (125, -1), (250, 2), (500, 1), (2000, 2), (4000, 3), (8000, 1)),
        Graphic("Headphones", (31, 3), (62, 2), (125, 1), (1000, -1), (2000, -2), (4000, 1), (8000, 3), (16000, 2)),
    ];
}
