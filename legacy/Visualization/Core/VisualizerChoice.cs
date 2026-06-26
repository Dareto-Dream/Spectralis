namespace Spectralis;

internal readonly record struct VisualizerChoice(string Key)
{
    public const string BuiltInPrefix = "builtin:";
    public const string InstalledPrefix = "installed:";
    public const string ScriptedPrefix = "script:";

    public static VisualizerChoice BuiltIn(VisualizerMode mode) => new($"{BuiltInPrefix}{mode}");

    public static VisualizerChoice Installed(string id) => new($"{InstalledPrefix}{id.Trim()}");

    public static VisualizerChoice Scripted(string id) => new($"{ScriptedPrefix}{id.Trim()}");

    public bool TryGetBuiltInMode(out VisualizerMode mode)
    {
        var key = NormalizeKey(Key);
        if (key.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Enum.TryParse(key[BuiltInPrefix.Length..], ignoreCase: true, out mode) &&
                Enum.IsDefined(mode);
        }

        if (Enum.TryParse(key, ignoreCase: true, out mode) && Enum.IsDefined(mode))
        {
            return true;
        }

        mode = VisualizerMode.MirrorSpectrum;
        return false;
    }

    public bool TryGetInstalledId(out string id)
    {
        var key = NormalizeKey(Key);
        if (key.StartsWith(InstalledPrefix, StringComparison.OrdinalIgnoreCase))
        {
            id = key[InstalledPrefix.Length..].Trim();
            return !string.IsNullOrWhiteSpace(id);
        }

        id = "";
        return false;
    }

    public bool TryGetScriptedId(out string id)
    {
        var key = NormalizeKey(Key);
        if (key.StartsWith(ScriptedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            id = key[ScriptedPrefix.Length..].Trim();
            return !string.IsNullOrWhiteSpace(id);
        }

        id = "";
        return false;
    }

    public VisualizerMode FallbackMode =>
        TryGetBuiltInMode(out var mode) ? mode : VisualizerMode.MirrorSpectrum;

    public string ToSettingsKey()
    {
        if (TryGetBuiltInMode(out var mode))
            return BuiltIn(mode).Key;

        if (TryGetInstalledId(out var installedId))
            return Installed(installedId).Key;

        if (TryGetScriptedId(out var scriptedId))
            return Scripted(scriptedId).Key;

        return BuiltIn(VisualizerMode.MirrorSpectrum).Key;
    }

    public static VisualizerChoice FromSettingsKey(string? value, VisualizerMode fallbackMode)
    {
        if (TryParse(value, out var choice))
            return choice;

        return BuiltIn(fallbackMode);
    }

    public static bool TryParse(string? value, out VisualizerChoice choice)
    {
        var key = NormalizeKey(value);
        if (string.IsNullOrWhiteSpace(key))
        {
            choice = default;
            return false;
        }

        if (key.StartsWith(InstalledPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var id = key[InstalledPrefix.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(id))
            {
                choice = Installed(id);
                return true;
            }
        }

        if (key.StartsWith(ScriptedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var id = key[ScriptedPrefix.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(id))
            {
                choice = Scripted(id);
                return true;
            }
        }

        if (key.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase))
            key = key[BuiltInPrefix.Length..].Trim();

        if (Enum.TryParse<VisualizerMode>(key, ignoreCase: true, out var mode) && Enum.IsDefined(mode))
        {
            choice = BuiltIn(mode);
            return true;
        }

        choice = default;
        return false;
    }

    public override string ToString() => ToSettingsKey();

    private static string NormalizeKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
}
