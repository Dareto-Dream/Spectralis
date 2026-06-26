namespace Spectralis.Core.Audio.Midi;

public static class MidiSoundFontLocator
{
    public static string ResolveDefaultSoundFontPath()
    {
        var relativePath = Path.Combine("Assets", "SoundFonts", "GeneralUser-GS.sf2");
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, relativePath),
            Path.Combine(Environment.CurrentDirectory, relativePath),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            "The bundled GeneralUser GS SoundFont could not be found. Rebuild Spectralis so Assets\\SoundFonts is copied to the output folder.",
            relativePath);
    }
}
