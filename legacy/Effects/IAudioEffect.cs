using NAudio.Wave;

namespace Spectralis;

internal interface IAudioEffect
{
    string           Name       { get; }
    bool             Enabled    { get; set; }
    EffectParameters Parameters { get; }

    ISampleProvider Wrap(ISampleProvider source);
}
