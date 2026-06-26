namespace Spectralis;

internal static class SharedPlayDefaults
{
    public const string LegacyCdnBaseUrl = "https://cdn.deltavdevs.com";
    public const string CdnBaseUrl = "https://audioplayer-production-5b83.up.railway.app";
    public const string ProtocolVersion = "shared-play-v1";
    public const string ClientName = "Spectralis";
    public const string RichPackageContentType = "application/vnd.spectralis.shared-play+zip";
    public const string WebSharePlayerPath = "/spectralis/web-share/index.html";
    public const string DiscordActivitySource = "discord";
    public const long MaxPackageBytes = 512L * 1024L * 1024L;

    public static string NormalizeCdnBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return CdnBaseUrl;
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    public static Uri BuildEndpoint(Uri cdnBaseUri, string relativePath)
    {
        var normalizedPath = relativePath.TrimStart('/');
        return new Uri(cdnBaseUri, normalizedPath);
    }

    public static Uri BuildWebShareJoinUrl(Uri cdnBaseUri, string sessionId)
    {
        var encodedSessionId = Uri.EscapeDataString(sessionId.Trim());
        return AddSessionQuery(new Uri(cdnBaseUri, WebSharePlayerPath.TrimStart('/')), encodedSessionId, null);
    }

    public static Uri BuildDiscordActivityJoinUrl(Uri cdnBaseUri, string sessionId)
    {
        var encodedSessionId = Uri.EscapeDataString(sessionId.Trim());
        return AddSessionQuery(
            new Uri(cdnBaseUri, WebSharePlayerPath.TrimStart('/')),
            encodedSessionId,
            DiscordActivitySource);
    }

    public static string ConvertToDiscordActivityJoinUrl(string joinUrl)
    {
        if (!Uri.TryCreate(joinUrl, UriKind.Absolute, out var uri))
            return joinUrl;

        var sessionId = TryReadSessionId(uri);
        if (string.IsNullOrWhiteSpace(sessionId))
            return joinUrl;

        return BuildDiscordActivityJoinUrl(new Uri(uri.GetLeftPart(UriPartial.Authority)), sessionId).ToString();
    }

    private static Uri AddSessionQuery(Uri playerUrl, string encodedSessionId, string? source)
    {
        var builder = new UriBuilder(playerUrl);
        var existingQuery = builder.Query.TrimStart('?');
        var sourceQuery = string.IsNullOrWhiteSpace(source)
            ? ""
            : $"&source={Uri.EscapeDataString(source)}&mode=activity";
        builder.Query = string.IsNullOrWhiteSpace(existingQuery)
            ? $"session={encodedSessionId}{sourceQuery}"
            : $"{existingQuery}&session={encodedSessionId}{sourceQuery}";
        return builder.Uri;
    }

    private static string? TryReadSessionId(Uri uri)
    {
        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            if (!string.Equals(key, "session", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "sessionId", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return parts.Length > 1
                ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')).Trim()
                : null;
        }

        return null;
    }
}
