using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Ordered rack of effects. Implements the engine's <see cref="IEffectChainBuilder"/>
/// seam so the engine can hot-swap the processed provider when the rack changes.
/// </summary>
public sealed class EffectChain : IEffectChainBuilder
{
    private readonly List<IAudioEffect> _effects = [];

    public IReadOnlyList<IAudioEffect> Effects => _effects;

    public bool Enabled { get; set; } = true;

    public event EventHandler? Changed;

    public void Add(IAudioEffect effect)
    {
        _effects.Add(effect);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(IAudioEffect effect)
    {
        _effects.Remove(effect);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MoveUp(int index)
    {
        if (index <= 0 || index >= _effects.Count)
        {
            return;
        }

        (_effects[index], _effects[index - 1]) = (_effects[index - 1], _effects[index]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MoveDown(int index)
    {
        if (index < 0 || index >= _effects.Count - 1)
        {
            return;
        }

        (_effects[index], _effects[index + 1]) = (_effects[index + 1], _effects[index]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public ISampleProvider BuildChain(ISampleProvider source)
    {
        if (!Enabled)
        {
            return source;
        }

        foreach (var effect in _effects)
        {
            if (effect.Enabled)
            {
                source = effect.Wrap(source);
            }
        }

        return source;
    }

    public static IAudioEffect CreateEffect(string displayName) => displayName switch
    {
        "10-Band EQ" => new Eq10BandEffect(),
        "Compressor" => new CompressorEffect(),
        "Reverb" => new ReverbEffect(),
        "Vocal Remover" => new VocalBlendEffect(),
        _ => throw new ArgumentException($"Unknown effect: {displayName}"),
    };

    public static string[] AvailableEffects { get; } =
        ["10-Band EQ", "Compressor", "Reverb", "Vocal Remover"];
}
