namespace Spectralis.Core.Audio.Midi;

public enum MidiPlaybackInstrument
{
    FilePrograms,
    AcousticGrandPiano,
    BrightAcousticPiano,
    ElectricPiano,
    DrawbarOrgan,
    RockOrgan,
    ChurchOrgan,
    Strings,
    Choir,
    SynthLead,
}

public sealed record MidiInstrumentOption(string Label, MidiPlaybackInstrument Value);

public static class MidiPlaybackInstrumentCatalog
{
    public static MidiInstrumentOption[] GetOptions() =>
    [
        new("Piano", MidiPlaybackInstrument.AcousticGrandPiano),
        new("Bright Piano", MidiPlaybackInstrument.BrightAcousticPiano),
        new("Electric Piano", MidiPlaybackInstrument.ElectricPiano),
        new("Organ", MidiPlaybackInstrument.DrawbarOrgan),
        new("Rock Organ", MidiPlaybackInstrument.RockOrgan),
        new("Church Organ", MidiPlaybackInstrument.ChurchOrgan),
        new("Strings", MidiPlaybackInstrument.Strings),
        new("Choir", MidiPlaybackInstrument.Choir),
        new("Synth Lead", MidiPlaybackInstrument.SynthLead),
        new("Use MIDI File", MidiPlaybackInstrument.FilePrograms),
    ];

    public static MidiPlaybackInstrument Normalize(MidiPlaybackInstrument instrument) =>
        Enum.IsDefined(instrument) ? instrument : MidiPlaybackInstrument.AcousticGrandPiano;

    public static int? GetProgram(MidiPlaybackInstrument instrument) =>
        Normalize(instrument) switch
        {
            MidiPlaybackInstrument.FilePrograms => null,
            MidiPlaybackInstrument.AcousticGrandPiano => 0,
            MidiPlaybackInstrument.BrightAcousticPiano => 1,
            MidiPlaybackInstrument.ElectricPiano => 4,
            MidiPlaybackInstrument.DrawbarOrgan => 16,
            MidiPlaybackInstrument.RockOrgan => 18,
            MidiPlaybackInstrument.ChurchOrgan => 19,
            MidiPlaybackInstrument.Strings => 48,
            MidiPlaybackInstrument.Choir => 52,
            MidiPlaybackInstrument.SynthLead => 80,
            _ => 0,
        };

    public static string GetLabel(MidiPlaybackInstrument instrument)
    {
        var normalized = Normalize(instrument);
        return GetOptions().FirstOrDefault(option => option.Value == normalized)?.Label ?? "Piano";
    }
}
