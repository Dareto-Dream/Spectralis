using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Spectralis.Core.Formats;

/// <summary>
/// The inspector's plain-text view of an event's parameters: one <c>key=value</c> per line. Values that
/// look like numbers or true/false are stored as such, everything else as text, so the saved JSON stays
/// natural (<c>"amount": 0.5</c>, not <c>"amount": "0.5"</c>).
/// </summary>
public static class ReactiveParamText
{
    public static string Format(IReadOnlyDictionary<string, object?> parameters)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in parameters)
        {
            sb.Append(key).Append('=').Append(ValueToText(value)).Append('\n');
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Lines without an <c>=</c> or with an empty key are ignored. Later duplicates win. Capped at the format limit.</summary>
    public static Dictionary<string, object?> Parse(string? text)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var at = raw.IndexOf('=');
            if (at <= 0) continue;

            var key = raw[..at].Trim();
            if (key.Length == 0) continue;
            if (!result.ContainsKey(key) && result.Count >= ReactiveFormat.MaxEventParams) break;

            result[key] = ParseValue(raw[(at + 1)..].Trim());
        }
        return result;
    }

    private static object? ParseValue(string value)
    {
        if (bool.TryParse(value, out var b)) return b;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)) return d;
        return value;
    }

    private static string ValueToText(object? value) => value switch
    {
        null => string.Empty,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString() ?? string.Empty,
        JsonElement { ValueKind: JsonValueKind.True } => "true",
        JsonElement { ValueKind: JsonValueKind.False } => "false",
        JsonElement { ValueKind: JsonValueKind.Null } => string.Empty,
        JsonElement e => e.GetRawText(),
        bool b => b ? "true" : "false",
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
