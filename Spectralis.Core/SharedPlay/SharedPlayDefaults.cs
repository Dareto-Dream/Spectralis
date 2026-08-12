using System.Text.RegularExpressions;

namespace Spectralis.Core.SharedPlay;

public static class SharedPlayDefaults
{
    public const string LegacyCdnBaseUrl = "https://cdn.deltavdevs.com";
    public const string CdnBaseUrl = "https://audioplayer-production-5b83.up.railway.app";
    public const string StagingCdnBaseUrl = "https://audioplayer-staging.up.railway.app";
    public const string ProtocolVersion = "shared-play-v2";
    public const string ClientName = "Spectralis";
    public const string RichPackageContentType = "application/vnd.spectralis.shared-play+zip";
    public const string WebSharePlayerPath = "/spectralis/web-share";
    public const string DiscordActivitySource = "discord";
    public const long MaxPackageBytes = 512L * 1024L * 1024L;

    private static readonly Regex RoomCodePattern = new(@"^[A-Z0-9]{6}$", RegexOptions.Compiled);

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

    public static Uri BuildWebShareJoinUrl(Uri cdnBaseUri, string roomCode)
    {
        var code = NormalizeRoomCode(roomCode) ?? roomCode.Trim();
        var encodedCode = Uri.EscapeDataString(code);
        return AddSessionQuery(new Uri(cdnBaseUri, WebSharePlayerPath.TrimStart('/')), encodedCode, null);
    }

    public static Uri BuildDiscordActivityJoinUrl(Uri cdnBaseUri, string roomCode)
    {
        var code = NormalizeRoomCode(roomCode) ?? roomCode.Trim();
        var encodedCode = Uri.EscapeDataString(code);
        return AddSessionQuery(
            new Uri(cdnBaseUri, WebSharePlayerPath.TrimStart('/')),
            encodedCode,
            DiscordActivitySource);
    }

    public static string ConvertToDiscordActivityJoinUrl(string joinUrl)
    {
        if (!Uri.TryCreate(joinUrl, UriKind.Absolute, out var uri))
            return joinUrl;

        var roomCode = TryReadRoomCode(uri);
        if (string.IsNullOrWhiteSpace(roomCode))
            return joinUrl;

        return BuildDiscordActivityJoinUrl(new Uri(uri.GetLeftPart(UriPartial.Authority)), roomCode).ToString();
    }

    /// <summary>Formats a raw room code as a display string with a dash: "X7K-29Q".</summary>
    public static string DisplayRoomCode(string code)
    {
        var raw = NormalizeRoomCode(code);
        if (raw is null || raw.Length != 6) return code;
        return $"{raw[..3]}-{raw[3..]}";
    }

    /// <summary>Strips dashes, uppercases, validates 6 alphanumeric chars. Returns null if invalid.</summary>
    public static string? NormalizeRoomCode(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var cleaned = new string(
            input.Trim()
                 .Where(c => char.IsAsciiLetterOrDigit(c))
                 .Select(char.ToUpperInvariant)
                 .Take(7)
                 .ToArray());
        return cleaned.Length == 6 ? cleaned : null;
    }

    /// <summary>Tries to read a room code from a query string param (session= or code=).</summary>
    public static string? TryReadRoomCode(Uri uri)
    {
        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            if (!string.Equals(key, "session", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "code", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "sessionId", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var raw = parts.Length > 1
                ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')).Trim()
                : null;
            return NormalizeRoomCode(raw);
        }
        return null;
    }

    private static Uri AddSessionQuery(Uri playerUrl, string encodedCode, string? source)
    {
        var builder = new UriBuilder(playerUrl);
        var existingQuery = builder.Query.TrimStart('?');
        var sourceQuery = string.IsNullOrWhiteSpace(source)
            ? ""
            : $"&source={Uri.EscapeDataString(source)}&mode=activity";
        builder.Query = string.IsNullOrWhiteSpace(existingQuery)
            ? $"session={encodedCode}{sourceQuery}"
            : $"{existingQuery}&session={encodedCode}{sourceQuery}";
        return builder.Uri;
    }
}
