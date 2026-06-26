namespace Spectralis;

public sealed record SharedPlayJoinRequest(string SessionId, string? CdnBaseUrl)
{
    private static readonly string[] SessionQueryKeys = ["session", "sessionId", "id"];
    private static readonly string[] CdnQueryKeys = ["cdn", "cdnBaseUrl", "baseUrl"];

    public static bool TryParse(string? input, bool allowRawSessionId, out SharedPlayJoinRequest request)
    {
        request = new SharedPlayJoinRequest("", null);
        var value = input?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return uri.Scheme switch
            {
                "spectralis" => TryParseUri(uri, null, out request),
                "http" or "https" => TryParseUri(uri, uri.GetLeftPart(UriPartial.Authority), out request),
                _ => false
            };
        }

        if (!allowRawSessionId)
            return false;

        var sessionId = CleanSessionId(value);
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        request = new SharedPlayJoinRequest(sessionId, null);
        return true;
    }

    private static bool TryParseUri(Uri uri, string? fallbackCdnBaseUrl, out SharedPlayJoinRequest request)
    {
        request = new SharedPlayJoinRequest("", null);
        var query = ParseQuery(uri.Query);
        var sessionId = FirstQueryValue(query, SessionQueryKeys);
        if (string.IsNullOrWhiteSpace(sessionId))
            sessionId = FindSessionIdInPath(uri);

        sessionId = CleanSessionId(sessionId);
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        var cdnBaseUrl = NormalizeCdnBaseUrl(FirstQueryValue(query, CdnQueryKeys)) ?? fallbackCdnBaseUrl;
        request = new SharedPlayJoinRequest(sessionId, cdnBaseUrl);
        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
            return values;

        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var value = parts.Length > 1
                ? Uri.UnescapeDataString(parts[1].Replace('+', ' '))
                : "";
            values[key] = value;
        }

        return values;
    }

    private static string? FirstQueryValue(Dictionary<string, string> query, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (query.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static string? FindSessionIdInPath(Uri uri)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(uri.Host))
            parts.Add(uri.Host);

        parts.AddRange(uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries));

        for (var index = 0; index < parts.Count; index++)
        {
            if (IsSessionPrefix(parts[index]) && index + 1 < parts.Count)
                return parts[index + 1];
        }

        return parts.Count == 1 ? parts[0] : null;
    }

    private static bool IsSessionPrefix(string value) =>
        string.Equals(value, "join", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "sessions", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "web-share", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeCdnBaseUrl(string? value)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }

        return null;
    }

    private static string CleanSessionId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var decoded = Uri.UnescapeDataString(value.Trim());
        var chars = decoded
            .Where(static character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '.' or '_' or ':' or '-')
            .ToArray();

        var cleaned = new string(chars);
        return IsReservedPathPart(cleaned) ? "" : cleaned;
    }

    private static bool IsReservedPathPart(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value, "shared-play", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "spectralis", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "web-share", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "join", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "open", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "sessions", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".htm", StringComparison.OrdinalIgnoreCase);
}
