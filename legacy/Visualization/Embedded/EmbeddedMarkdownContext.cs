namespace Spectralis;

public sealed class EmbeddedMarkdownContext
{
    internal EmbeddedMarkdownContext(string id, byte[] markdownBytes, string? cssOverride, string? version)
    {
        Id = id;
        MarkdownBytes = markdownBytes;
        CssOverride = cssOverride;
        Version = version;
    }

    internal string Id { get; }
    internal byte[] MarkdownBytes { get; }
    internal string? CssOverride { get; }
    internal string? Version { get; }

    public string DisplayName => CreateDisplayLabel(Id);

    private static string CreateDisplayLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Markdown Content";
        }

        return string.Join(
            ' ',
            value
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
