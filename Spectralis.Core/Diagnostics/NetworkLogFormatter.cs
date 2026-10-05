using System.Globalization;
using System.Text;

namespace Spectralis.Core.Diagnostics;

/// <summary>Turns entries into the text the Network tab shows and copies, and decides which entries a filter keeps.</summary>
public static class NetworkLogFormatter
{
    public static string StatusText(NetworkEntry e) => e.State switch
    {
        NetworkState.Pending => "...",
        NetworkState.Live => "LIVE",
        NetworkState.Failed => "ERR",
        _ => e.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "ok",
    };

    public static string Duration(double? ms) => ms switch
    {
        null => string.Empty,
        < 1000 => $"{ms.Value:0} ms",
        _ => $"{ms.Value / 1000:0.0#} s",
    };

    public static string Bytes(long? bytes) => bytes switch
    {
        null => string.Empty,
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes.Value / 1024.0:0.#} KB",
        _ => $"{bytes.Value / 1048576.0:0.##} MB",
    };

    /// <summary>Size column: response size for HTTP, frames in/out for sockets.</summary>
    public static string SizeText(NetworkEntry e) => e.Kind == NetworkKind.WebSocket
        ? $"{e.FramesReceived} in / {e.FramesSent} out"
        : Bytes(e.ResponseBytes);

    public static string PathAndQuery(NetworkEntry e)
    {
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri)) return e.Url;
        var path = uri.PathAndQuery;
        return path.Length == 0 ? "/" : path;
    }

    /// <summary>True when the entry passes the filter. Text matches any of source, method, host, URL and status, all words required.</summary>
    public static bool Matches(NetworkEntry e, string? text, bool errorsOnly, string? source)
    {
        if (errorsOnly && !e.IsError) return false;
        if (!string.IsNullOrEmpty(source) && !string.Equals(e.Source, source, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var haystack = $"{e.Source} {e.Method} {e.Url} {StatusText(e)} {e.Error}";
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => haystack.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    public static string Detail(NetworkEntry e)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{e.Method} {e.Url}");
        sb.AppendLine($"source    {e.Source}  ({e.Kind})");
        sb.AppendLine($"started   {e.StartedAt:yyyy-MM-dd HH:mm:ss.fff zzz}");
        if (e.DurationMs is not null) sb.AppendLine($"took      {Duration(e.DurationMs)}");
        sb.AppendLine($"state     {e.State}{(e.StatusCode is { } code ? $", {code} {e.Reason}" : string.Empty)}");
        if (e.Error is not null) sb.AppendLine($"error     {e.Error}");
        if (e.ContentType is not null) sb.AppendLine($"type      {e.ContentType}");
        if (e.RequestBytes is not null) sb.AppendLine($"sent      {Bytes(e.RequestBytes)}");
        if (e.ResponseBytes is not null) sb.AppendLine($"received  {Bytes(e.ResponseBytes)}");
        if (e.Kind == NetworkKind.WebSocket)
        {
            sb.AppendLine($"frames    {e.FramesReceived} in ({Bytes(e.BytesReceived)}), {e.FramesSent} out ({Bytes(e.BytesSent)})");
        }
        AppendHeaders(sb, "request headers", e.RequestHeaders);
        AppendHeaders(sb, "response headers", e.ResponseHeaders);
        return sb.ToString().TrimEnd();
    }

    public static string Line(NetworkEntry e) =>
        $"{e.StartedAt:HH:mm:ss.fff}  {e.Method,-6} {StatusText(e),-4} {Duration(e.DurationMs),8}  [{e.Source}] {e.Url}{(e.Error is null ? string.Empty : $"  !! {e.Error}")}";

    /// <summary>One line per entry, oldest first, for pasting into a bug report. Secrets were masked when the entries were recorded.</summary>
    public static string Report(IEnumerable<NetworkEntry> entries) => string.Join(Environment.NewLine, entries.Select(Line));

    private static void AppendHeaders(StringBuilder sb, string title, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (headers.Count == 0) return;
        sb.AppendLine().AppendLine(title);
        foreach (var h in headers) sb.AppendLine($"  {h.Key}: {h.Value}");
    }
}
