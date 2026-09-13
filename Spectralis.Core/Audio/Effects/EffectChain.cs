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
        "Parametric EQ" => new ParametricEqEffect(),
        "10-Band EQ" => new ParametricEqEffect(),  // legacy persisted name
        "Compressor" => new CompressorEffect(),
        "Reverb" => new ReverbEffect(),
        // Not in AvailableEffects (so it no longer shows in the "add effect" picker),
        // but kept creatable so Karaoke Mode's own VocalBlendEffect usage and any
        // previously persisted rack containing it keep working.
        "Vocal Remover" => new VocalBlendEffect(),
        "Saturation" => new SaturationEffect(),
        "Distortion" => new DistortionEffect(),
        "Chorus" => new ChorusEffect(),
        "Flanger" => new FlangerEffect(),
        "Phaser" => new PhaserEffect(),
        "Stereo Widener" => new StereoWidenerEffect(),
        "Limiter" => new LimiterEffect(),
        "Noise Gate" => new NoiseGateEffect(),
        "De-esser" => new DeEsserEffect(),
        "Multiband Compressor" => new MultibandCompressorEffect(),
        "Transient Shaper" => new TransientShaperEffect(),
        "Convolution Reverb" => new ConvolutionReverbEffect(),
        "Delay" => new DelayEffect(),
        "Stereo Panner" => new StereoPannerEffect(),
        "Room Ambience" => new RoomAmbienceEffect(),
        _ => throw new ArgumentException($"Unknown effect: {displayName}"),
    };

    public static string[] AvailableEffects { get; } =
    [
        "Parametric EQ", "Compressor", "Reverb",
        "Saturation", "Distortion", "Chorus", "Flanger", "Phaser", "Stereo Widener",
        "Limiter", "Noise Gate", "De-esser", "Multiband Compressor", "Transient Shaper",
        "Convolution Reverb", "Delay", "Stereo Panner", "Room Ambience",
    ];
}
