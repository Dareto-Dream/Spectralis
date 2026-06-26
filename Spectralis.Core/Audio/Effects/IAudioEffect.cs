using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

public interface IAudioEffect
{
    string Name { get; }
    bool Enabled { get; set; }
    EffectParameters Parameters { get; }

    ISampleProvider Wrap(ISampleProvider source);
}

public sealed class EffectParameters
{
    private readonly Dictionary<string, float> _values = [];

    public float Get(string key, float defaultValue = 0f) =>
        _values.TryGetValue(key, out var v) ? v : defaultValue;

    public void Set(string key, float value) => _values[key] = value;

    public IReadOnlyDictionary<string, float> All => _values;

    public EffectParameters Clone()
    {
        var clone = new EffectParameters();
        foreach (var kv in _values)
        {
            clone._values[kv.Key] = kv.Value;
        }

        return clone;
    }
}
