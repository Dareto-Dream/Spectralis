using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis.Core.Platform;

/// <summary>
/// Deserialized from https://cdn.deltavdevs.com/spectralis/warning.json.
/// Lets us push an urgent notice to users without shipping an app update.
/// </summary>
public sealed class CdnWarning
{
    /// <summary>
    /// Stable identifier used to suppress the warning after the user dismisses it.
    /// Omit (or leave null) to show the warning on every boot regardless of dismissal.
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>Heading shown in the dialog.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "Notice";

    /// <summary>Body text shown in the dialog. Supports newlines (\n).</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>"info" | "warning" | "critical". Affects the visual treatment.</summary>
    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "warning";

    /// <summary>
    /// If set, only show the warning to users on this exact list of version strings.
    /// Takes priority over MinVersion / MaxVersion when present.
    /// </summary>
    [JsonPropertyName("versions")]
    public List<string>? Versions { get; init; }

    /// <summary>Inclusive lower bound. Null = no lower bound.</summary>
    [JsonPropertyName("minVersion")]
    public string? MinVersion { get; init; }

    /// <summary>Inclusive upper bound. Null = no upper bound.</summary>
    [JsonPropertyName("maxVersion")]
    public string? MaxVersion { get; init; }

    /// <summary>
    /// Set to false to retire a warning without removing it from the file.
    /// Inactive warnings are never shown regardless of version or dismissal state.
    /// </summary>
    [JsonPropertyName("active")]
    public bool Active { get; init; } = true;

    /// <summary>
    /// Whether the user can dismiss the dialog without taking action.
    /// If false, the only options are to follow the link or close the application.
    /// </summary>
    [JsonPropertyName("dismissible")]
    public bool Dismissible { get; init; } = true;

    /// <summary>Label for the action button. Defaults to "Learn More" when LinkUrl is set.</summary>
    [JsonPropertyName("linkLabel")]
    public string? LinkLabel { get; init; }

    /// <summary>URL to open when the user clicks the action button. No button shown when absent.</summary>
    [JsonPropertyName("linkUrl")]
    public string? LinkUrl { get; init; }

    public bool HasLink => !string.IsNullOrWhiteSpace(LinkUrl);

    public bool AppliesToVersion(string? currentVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion)) return true;
        if (!Version.TryParse(currentVersion, out var current)) return true;

        if (Versions is { Count: > 0 })
            return Versions.Any(v => Version.TryParse(v, out var parsed) && parsed == current);

        if (!string.IsNullOrWhiteSpace(MinVersion) && Version.TryParse(MinVersion, out var min) && current < min)
            return false;
        if (!string.IsNullOrWhiteSpace(MaxVersion) && Version.TryParse(MaxVersion, out var max) && current > max)
            return false;

        return true;
    }
}

public static class CdnWarningClient
{
    private const string WarningUrl = "https://cdn.deltavdevs.com/spectralis/warning.json";

    /// <summary>
    /// Fetches warning.json and returns every warning that is active, applies to
    /// the current version, and has not already been dismissed by the user.
    /// Returns an empty list on network failure, 404, or malformed JSON.
    /// </summary>
    public static async Task<IReadOnlyList<CdnWarning>> FetchAsync(
        string? currentVersion,
        IReadOnlyCollection<string> dismissedIds,
        CancellationToken ct = default)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var json = await http.GetStringAsync(WarningUrl, ct);
            var warnings = JsonSerializer.Deserialize<List<CdnWarning>>(json);
            if (warnings is null) return [];

            return warnings
                .Where(w => w.Active)
                .Where(w => !string.IsNullOrWhiteSpace(w.Message))
                .Where(w => w.AppliesToVersion(currentVersion))
                .Where(w => w.Id is null || !dismissedIds.Contains(w.Id))
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
