namespace Spectralis.Core.SharedPlay;

/// <summary>Parses a room code or join URL into a normalized 6-char alphanumeric room code.</summary>
public sealed record SharedPlayJoinRequest(string RoomCode, string? CdnBaseUrl)
{
    private static readonly string[] SessionQueryKeys = ["session", "code", "sessionId", "id"];
    private static readonly string[] CdnQueryKeys = ["cdn", "cdnBaseUrl", "baseUrl"];

    public string DisplayCode => SharedPlayDefaults.DisplayRoomCode(RoomCode);

    public static bool TryParse(string? input, bool allowRawCode, out SharedPlayJoinRequest request)
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

        if (!allowRawCode)
            return false;

        // Accept plain room code with or without dash: "X7K29Q" or "X7K-29Q"
        var roomCode = SharedPlayDefaults.NormalizeRoomCode(value);
        if (string.IsNullOrWhiteSpace(roomCode))
            return false;

        request = new SharedPlayJoinRequest(roomCode, null);
        return true;
    }

    private static bool TryParseUri(Uri uri, string? fallbackCdnBaseUrl, out SharedPlayJoinRequest request)
    {
        request = new SharedPlayJoinRequest("", null);
        var query = ParseQuery(uri.Query);

        // Prefer explicit query param first
        var rawCode = FirstQueryValue(query, SessionQueryKeys);

        // Fall back to path segment
        if (string.IsNullOrWhiteSpace(rawCode))
            rawCode = FindCodeInPath(uri);

        var roomCode = SharedPlayDefaults.NormalizeRoomCode(rawCode);
        if (string.IsNullOrWhiteSpace(roomCode))
            return false;

        var cdnBaseUrl = NormalizeCdnBaseUrl(FirstQueryValue(query, CdnQueryKeys)) ?? fallbackCdnBaseUrl;
        request = new SharedPlayJoinRequest(roomCode, cdnBaseUrl);
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
            if (string.IsNullOrWhiteSpace(key)) continue;
            var val = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            values[key] = val;
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

    private static string? FindCodeInPath(Uri uri)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(uri.Host))
            parts.Add(uri.Host);
        parts.AddRange(uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries));

        for (var i = 0; i < parts.Count; i++)
        {
            if (IsSessionPrefix(parts[i]) && i + 1 < parts.Count)
                return parts[i + 1];
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
}
