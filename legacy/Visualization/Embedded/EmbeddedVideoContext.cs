namespace Spectralis;

public sealed class EmbeddedVideoContext
{
    internal EmbeddedVideoContext(
        string id,
        string codec,
        byte[] videoBytes,
        int? width,
        int? height,
        bool autoplay,
        bool loop,
        string? version)
    {
        Id = id;
        Codec = codec;
        VideoBytes = videoBytes;
        Width = width;
        Height = height;
        Autoplay = autoplay;
        Loop = loop;
        Version = version;
    }

    internal string Id { get; }
    internal string Codec { get; }
    internal byte[] VideoBytes { get; }
    internal int? Width { get; }
    internal int? Height { get; }
    internal bool Autoplay { get; }
    internal bool Loop { get; }
    internal string? Version { get; }

    public string DisplayName => CreateDisplayLabel(Id);

    private static string CreateDisplayLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Video Content";
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
