using NAudio.Wave;

namespace Spectralis.Core.Audio;

/// <summary>
/// Hook for the effects rack: given the raw decoded provider, return the
/// processed provider. The engine rebuilds the chain on demand (hot-swap).
/// </summary>
public interface IEffectChainBuilder
{
    ISampleProvider BuildChain(ISampleProvider source);
}
