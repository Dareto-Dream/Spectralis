using System.Text.Json;
using System.Text.Json.Serialization;
using Spectralis.Core.Audio.Effects;

namespace Spectralis.App.Services;

/// <summary>
/// Serializes the effects rack (order, per-effect enable state, and every
/// <see cref="EffectParameters"/> value) to JSON so it survives a restart.
/// Stored in <c>AppSettings.EffectChainJson</c>.
/// </summary>
public static class EffectChainState
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(EffectChain chain)
    {
        var model = new ChainModel
        {
            Enabled = chain.Enabled,
            Effects = chain.Effects.Select(e => new EffectModel
            {
                Name = e.Name,
                Enabled = e.Enabled,
                Params = e.Parameters.All.ToDictionary(kv => kv.Key, kv => kv.Value),
            }).ToList(),
        };
        return JsonSerializer.Serialize(model, Options);
    }

    public static void Restore(EffectChain chain, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        ChainModel? model;
        try
        {
            model = JsonSerializer.Deserialize<ChainModel>(json, Options);
        }
        catch
        {
            return;
        }

        if (model is null)
        {
            return;
        }

        foreach (var effect in chain.Effects.ToList())
        {
            chain.Remove(effect);
        }

        foreach (var effectModel in model.Effects ?? [])
        {
            IAudioEffect effect;
            try
            {
                effect = EffectChain.CreateEffect(effectModel.Name);
            }
            catch (ArgumentException)
            {
                continue;
            }

            effect.Enabled = effectModel.Enabled;
            foreach (var (key, value) in effectModel.Params ?? [])
            {
                effect.Parameters.Set(key, value);
            }

            chain.Add(effect);
        }

        chain.Enabled = model.Enabled;
    }

    private sealed class ChainModel
    {
        public bool Enabled { get; set; } = true;
        public List<EffectModel>? Effects { get; set; }
    }

    private sealed class EffectModel
    {
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public Dictionary<string, float>? Params { get; set; }
    }
}
