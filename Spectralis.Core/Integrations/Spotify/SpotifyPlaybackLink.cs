namespace Spectralis.Core.Integrations.Spotify;

public sealed record SpotifyPlaybackLink(string PlaybackUri, Uri WebUrl, bool IsContext)
{
    public static bool TryParse(string? input, out SpotifyPlaybackLink link)
    {
        link = null!;
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value)) return false;

        string[] parts;
        if (value.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            parts = value.Split(':');
            if (parts.Length != 3) return false;
            parts = parts[1..];
        }
        else
        {
            if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("https" or "http") ||
                !uri.Host.Equals("open.spotify.com", StringComparison.OrdinalIgnoreCase)) return false;
            parts = uri.AbsolutePath.Trim('/').Split('/');
            if (parts.Length == 3 && parts[0].StartsWith("intl-", StringComparison.OrdinalIgnoreCase))
                parts = parts[1..];
            if (parts.Length != 2) return false;
        }

        var kind = parts[0].ToLowerInvariant();
        var id = parts[1];
        if (kind is not ("track" or "album" or "playlist" or "artist" or "episode") ||
            id.Length != 22 || !id.All(char.IsAsciiLetterOrDigit)) return false;
        link = new SpotifyPlaybackLink($"spotify:{kind}:{id}",
            new Uri($"https://open.spotify.com/{kind}/{id}"),
            kind is "album" or "playlist" or "artist");
        return true;
    }
}
