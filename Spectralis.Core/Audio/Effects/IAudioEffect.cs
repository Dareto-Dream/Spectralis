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

    /// <summary>
    /// Bumped whenever a value actually changes. Effects that re-read their
    /// parameters per audio block (e.g. <see cref="ParametricEqEffect"/>) watch
    /// this so edits apply without a full engine rebuild.
    /// </summary>
    public int Revision { get; private set; }

    public float Get(string key, float defaultValue = 0f) =>
        _values.TryGetValue(key, out var v) ? v : defaultValue;

    public void Set(string key, float value)
    {
        if (_values.TryGetValue(key, out var existing) && existing.Equals(value))
        {
            return;
        }

        _values[key] = value;
        Revision++;
    }

    public IReadOnlyDictionary<string, float> All => _values;

    public EffectParameters Clone()
    {
        var clone = new EffectParameters();
        foreach (var kv in _values)
        {
            clone._values[kv.Key] = kv.Value;
        }

        clone.Revision = Revision;
        return clone;
    }
}
