namespace Spectralis;

internal sealed record BeatGrid(
    float   Bpm,
    TimeSpan FirstBeatOffset,
    int     BeatsPerBar,
    string  Key);
