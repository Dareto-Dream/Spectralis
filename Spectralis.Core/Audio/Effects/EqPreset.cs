namespace Spectralis.Core.Audio.Effects;

/// <summary>One band of a parametric EQ.</summary>
public sealed record EqBand(
    float Frequency,
    float GainDb,
    float Q,
    EqFilterType Type,
    bool Enabled = true);

/// <summary>
/// A named parametric-EQ configuration: preamp, output gain, and an ordered band list.
/// Built-in presets live in <see cref="EqPresets"/>; user presets are persisted by
/// the app's <c>EqPresetStore</c>.
/// </summary>
public sealed record EqPreset(
    string Name,
    float PreampDb,
    float OutputGainDb,
    IReadOnlyList<EqBand> Bands)
{
    public void ApplyTo(ParametricEqEffect effect) => effect.LoadPreset(this);

    public static EqPreset FromEffect(ParametricEqEffect effect, string name) =>
        new(name, effect.PreampDb, effect.OutputGainDb, effect.ReadBands());
}
