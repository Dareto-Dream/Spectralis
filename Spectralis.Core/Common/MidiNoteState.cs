namespace Spectralis.Core.Common;

public sealed record MidiNoteState(
    int Note,
    int Velocity,
    int Channel,
    float AgeSeconds,
    float StartSeconds = 0,
    float EndSeconds = 0);
