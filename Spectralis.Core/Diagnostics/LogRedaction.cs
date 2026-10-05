using System.Text.RegularExpressions;

namespace Spectralis.Core.Diagnostics;

/// <summary>
/// What the network log is allowed to keep. URLs and headers carry tokens, keys and cookies, and a log that is
/// copied into a bug report must never hold one, so masking happens before anything is stored.
/// </summary>
public static partial class LogRedaction
{
    public const string Mask = "***";

    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization", "proxy-authorization", "cookie", "set-cookie", "x-session-key", "x-api-key",
        "x-auth-token", "x-csrf-token", "x-owner-token", "x-admin-actor",
    };

    [GeneratedRegex("token|key|secret|code|auth|sig|password|passwd|pwd|session|bearer|credential|cookie", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveName();

    public static bool IsSensitiveName(string name) => SensitiveName().IsMatch(name);

    /// <summary>The URL as a string, with credentials in the address removed and sensitive query values masked.</summary>
    public static string Url(Uri? uri)
    {
        if (uri is null) return string.Empty;
        if (!uri.IsAbsoluteUri) return uri.OriginalString;

        var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
        if (!string.IsNullOrEmpty(uri.Query))
        {
            var pairs = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair =>
                {
                    var eq = pair.IndexOf('=');
                    if (eq < 0) return pair;
                    var name = pair[..eq];
                    return IsSensitiveName(Uri.UnescapeDataString(name.Replace('+', ' '))) ? $"{name}={Mask}" : pair;
                });
            builder.Query = string.Join('&', pairs);
        }
        return builder.Uri.AbsoluteUri;
    }

    /// <summary>Header value with credentials masked; "Bearer abc" keeps its scheme so you can see how a call was authorised.</summary>
    public static string HeaderValue(string name, string value)
    {
        if (!SensitiveHeaders.Contains(name)) return value;
        var space = value.IndexOf(' ');
        return space > 0 && space < 16 && name.EndsWith("authorization", StringComparison.OrdinalIgnoreCase)
            ? $"{value[..space]} {Mask}"
            : Mask;
    }

    public static IReadOnlyList<KeyValuePair<string, string>> Headers(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers) =>
        headers.Select(h => new KeyValuePair<string, string>(h.Key, HeaderValue(h.Key, string.Join(", ", h.Value))))
            .OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
