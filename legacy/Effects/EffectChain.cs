using NAudio.Wave;
using System.Text.Json;

namespace Spectralis;

internal sealed class EffectChain
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
        if (index <= 0 || index >= _effects.Count) return;
        (_effects[index], _effects[index - 1]) = (_effects[index - 1], _effects[index]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MoveDown(int index)
    {
        if (index < 0 || index >= _effects.Count - 1) return;
        (_effects[index], _effects[index + 1]) = (_effects[index + 1], _effects[index]);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public ISampleProvider BuildChain(ISampleProvider source)
    {
        if (!Enabled) return source;

        foreach (var effect in _effects)
        {
            if (effect.Enabled)
                source = effect.Wrap(source);
        }
        return source;
    }

    // ── Preset serialization ──────────────────────────────────────────────────

    public EffectPreset ToPreset(string name) => new()
    {
        Name    = name,
        Enabled = Enabled,
        Effects = _effects.Select(e => new EffectState
        {
            TypeName   = e.GetType().Name,
            Enabled    = e.Enabled,
            Parameters = e.Parameters.All.ToDictionary(kv => kv.Key, kv => kv.Value),
        }).ToList(),
    };

    public void ApplyPreset(EffectPreset preset)
    {
        Enabled = preset.Enabled;
        _effects.Clear();

        foreach (var state in preset.Effects)
        {
            var effect = CreateByTypeName(state.TypeName);
            if (effect is null) continue;
            effect.Enabled = state.Enabled;
            foreach (var kv in state.Parameters)
                effect.Parameters.Set(kv.Key, kv.Value);
            _effects.Add(effect);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static IAudioEffect? CreateByTypeName(string typeName) => typeName switch
    {
        nameof(Eq10BandEffect)    => new Eq10BandEffect(),
        nameof(CompressorEffect)  => new CompressorEffect(),
        nameof(ReverbEffect)      => new ReverbEffect(),
        nameof(VocalBlendEffect)  => new VocalBlendEffect(),
        _ => null,
    };

    // ── Static factories ──────────────────────────────────────────────────────

    public static IAudioEffect CreateEffect(string displayName) => displayName switch
    {
        "10-Band EQ"     => new Eq10BandEffect(),
        "Compressor"     => new CompressorEffect(),
        "Reverb"         => new ReverbEffect(),
        "Vocal Remover"  => new VocalBlendEffect(),
        _ => throw new ArgumentException($"Unknown effect: {displayName}"),
    };

    public static string[] AvailableEffects { get; } =
        ["10-Band EQ", "Compressor", "Reverb", "Vocal Remover"];
}

internal sealed class EffectPreset
{
    public string            Name    { get; set; } = "";
    public bool              Enabled { get; set; } = true;
    public List<EffectState> Effects { get; set; } = [];
}

internal sealed class EffectState
{
    public string                    TypeName   { get; set; } = "";
    public bool                      Enabled    { get; set; } = true;
    public Dictionary<string, float> Parameters { get; set; } = [];
}
