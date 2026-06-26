namespace Spectralis;

public sealed class EmbeddedThemeContext
{
    private readonly string displayName;

    internal EmbeddedThemeContext(ThemeMode mode, ThemeAccent accent, string? displayName, string? version)
    {
        Mode = mode;
        Accent = accent;
        Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        this.displayName = string.IsNullOrWhiteSpace(displayName)
            ? $"{accent} {mode}"
            : displayName.Trim();
    }

    internal ThemeMode Mode { get; }

    internal ThemeAccent Accent { get; }

    internal string? Version { get; }

    public string DisplayName => displayName;
}
