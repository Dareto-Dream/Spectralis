namespace Spectralis;

public sealed class EmbeddedHtmlContext
{
    internal EmbeddedHtmlContext(
        string id,
        byte[] htmlBytes,
        IReadOnlyDictionary<string, byte[]> binaryAssets,
        IReadOnlyDictionary<string, string>? textAssets,
        string? version)
    {
        Id = id;
        HtmlBytes = htmlBytes;
        BinaryAssets = binaryAssets.ToDictionary(
            static item => item.Key,
            static item => item.Value.ToArray(),
            StringComparer.OrdinalIgnoreCase);
        TextAssets = (textAssets ?? new Dictionary<string, string>())
            .ToDictionary(
                static item => item.Key,
                static item => item.Value,
                StringComparer.OrdinalIgnoreCase);
        Version = version;
    }

    internal string Id { get; }
    internal byte[] HtmlBytes { get; }
    internal IReadOnlyDictionary<string, byte[]> BinaryAssets { get; }
    internal IReadOnlyDictionary<string, string> TextAssets { get; }
    internal string? Version { get; }

    public string DisplayName => CreateDisplayLabel(Id);

    private static string CreateDisplayLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "HTML Content";
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
