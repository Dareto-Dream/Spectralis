using System.Text.Json.Serialization;

namespace Spectralis.Core.Formats;

public static class ReactiveFormat
{
    public const string FormatName = "spectralis-track-reactive";
    public const int FormatVersion = 3;

    // Structural caps enforced before a document is accepted (untrusted input).
    public const int MaxSections = 2048;
    public const int MaxTimelineEvents = 16384;
    public const int MaxEventParams = 256;
    public const long MaxSidecarBytes = 8 * 1024 * 1024;
}

public sealed class ReactiveTimelineDocument
{
    // No valid-by-default: untrusted JSON must declare format + version explicitly.
    [JsonPropertyName("format")] public string Format { get; set; } = string.Empty;
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; }
    [JsonPropertyName("sections")] public List<ReactiveSection> Sections { get; set; } = [];
    [JsonPropertyName("assets")] public List<ReactiveAssetRef> Assets { get; set; } = [];
    [JsonPropertyName("shaderPacks")] public List<ReactiveShaderPack> ShaderPacks { get; set; } = [];
    [JsonPropertyName("timeline")] public List<ReactiveTimelineEvent> Timeline { get; set; } = [];

    public bool IsValid() =>
        string.Equals(Format, ReactiveFormat.FormatName, StringComparison.Ordinal)
        && FormatVersion == ReactiveFormat.FormatVersion
        && Sections.Count <= ReactiveFormat.MaxSections
        && Timeline.Count <= ReactiveFormat.MaxTimelineEvents
        && Timeline.All(static evt =>
            double.IsFinite(evt.Time) && evt.Time >= 0 &&
            double.IsFinite(evt.Duration) && evt.Duration >= 0 &&
            evt.Params.Count <= ReactiveFormat.MaxEventParams)
        && Sections.All(static section =>
            double.IsFinite(section.Start) && double.IsFinite(section.End) && section.Start <= section.End);
}

public sealed class ReactiveSection
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("start")] public double Start { get; set; }
    [JsonPropertyName("end")] public double End { get; set; }
    [JsonPropertyName("mood")] public string Mood { get; set; } = "";
}

public sealed class ReactiveTimelineEvent
{
    [JsonPropertyName("time")] public double Time { get; set; }
    [JsonPropertyName("target")] public string Target { get; set; } = "";
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("duration")] public double Duration { get; set; }
    [JsonPropertyName("easing")] public string Easing { get; set; } = "linear";
    [JsonPropertyName("params")] public Dictionary<string, object?> Params { get; set; } = [];
}

public sealed class ReactiveAssetRef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

public sealed class ReactiveShaderPack
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("shader")] public string Shader { get; set; } = "";
    [JsonPropertyName("inputs")] public Dictionary<string, ReactiveShaderInput> Inputs { get; set; } = [];
}

public sealed class ReactiveShaderInput
{
    [JsonPropertyName("type")] public string Type { get; set; } = "number";
    [JsonPropertyName("min")] public double? Min { get; set; }
    [JsonPropertyName("max")] public double? Max { get; set; }
    [JsonPropertyName("default")] public object? Default { get; set; }
}

public sealed record ReactiveRuntimeState(
    ReactiveSection? CurrentSection,
    IReadOnlyList<ReactiveTimelineEvent> ActiveTransitions,
    IReadOnlyDictionary<string, object?> CurrentParams);
