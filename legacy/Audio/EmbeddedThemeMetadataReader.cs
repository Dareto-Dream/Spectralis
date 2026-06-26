using System.Text.Json.Nodes;
using TagLib;
using TagLib.Id3v2;

namespace Spectralis;

internal static class EmbeddedThemeMetadataReader
{
    private const string ThemePrefix = "DELTA_THEME_";
    private const string ThemeDescription = "DELTA_THEME";

    public static EmbeddedThemeContext? Read(TagLib.Id3v2.Tag? id3Tag)
    {
        if (id3Tag is null)
        {
            return null;
        }

        foreach (var frame in id3Tag.GetFrames<UserTextInformationFrame>())
        {
            var description = Normalize(frame.Description);
            var payload = JoinFrameText(frame.Text);
            if (string.IsNullOrWhiteSpace(description) ||
                string.IsNullOrWhiteSpace(payload) ||
                !IsThemeDescription(description))
            {
                continue;
            }

            var theme = TryParseTheme(payload);
            if (theme is not null)
            {
                return theme;
            }
        }

        return null;
    }

    private static bool IsThemeDescription(string description) =>
        string.Equals(description, ThemeDescription, StringComparison.OrdinalIgnoreCase) ||
        description.StartsWith(ThemePrefix, StringComparison.OrdinalIgnoreCase);

    private static EmbeddedThemeContext? TryParseTheme(string payload)
    {
        if (TryParseJson(payload) is not JsonObject jsonObject)
        {
            return null;
        }

        var type = ReadOptionalString(jsonObject, "type");
        if (!string.IsNullOrWhiteSpace(type) &&
            !string.Equals(type, "theme", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!TryReadThemeMode(jsonObject, out var mode) ||
            !TryReadThemeAccent(jsonObject, out var accent))
        {
            return null;
        }

        return new EmbeddedThemeContext(
            mode,
            accent,
            ReadOptionalString(jsonObject, "name") ??
            ReadOptionalString(jsonObject, "label") ??
            ReadOptionalString(jsonObject, "id"),
            ReadOptionalString(jsonObject, "version"));
    }

    private static bool TryReadThemeMode(JsonObject jsonObject, out ThemeMode mode) =>
        TryReadEnum(jsonObject, out mode, "mode", "themeMode");

    private static bool TryReadThemeAccent(JsonObject jsonObject, out ThemeAccent accent) =>
        TryReadEnum(jsonObject, out accent, "accent", "themeAccent");

    private static bool TryReadEnum<TEnum>(JsonObject jsonObject, out TEnum value, params string[] propertyNames)
        where TEnum : struct, Enum
    {
        foreach (var propertyName in propertyNames)
        {
            var rawValue = ReadOptionalString(jsonObject, propertyName);
            if (!string.IsNullOrWhiteSpace(rawValue) &&
                Enum.TryParse<TEnum>(rawValue, ignoreCase: true, out value) &&
                Enum.IsDefined(value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static JsonNode? TryParseJson(string value)
    {
        try
        {
            return JsonNode.Parse(value);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadOptionalString(JsonObject jsonObject, string propertyName)
    {
        if (jsonObject[propertyName] is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var stringValue) &&
            !string.IsNullOrWhiteSpace(stringValue))
        {
            return stringValue.Trim();
        }

        return null;
    }

    private static string? JoinFrameText(string[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return null;
        }

        var normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();

        return normalized.Length == 0 ? null : string.Join(Environment.NewLine, normalized);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
