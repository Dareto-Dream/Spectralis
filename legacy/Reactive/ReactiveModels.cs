using System.Text.Json.Serialization;

namespace Spectralis;

internal static class ReactiveFormat
{
    public const string FormatName = "spectralis-track-reactive";
    public const int FormatVersion = 3;
}

internal sealed class ReactiveTimelineDocument
{
    [JsonPropertyName("format")] public string Format { get; set; } = ReactiveFormat.FormatName;
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = ReactiveFormat.FormatVersion;
    [JsonPropertyName("sections")] public List<ReactiveSection> Sections { get; set; } = [];
    [JsonPropertyName("assets")] public List<ReactiveAssetRef> Assets { get; set; } = [];
    [JsonPropertyName("shaderPacks")] public List<ReactiveShaderPack> ShaderPacks { get; set; } = [];
    [JsonPropertyName("timeline")] public List<ReactiveTimelineEvent> Timeline { get; set; } = [];

    public bool IsValid() =>
        string.Equals(Format, ReactiveFormat.FormatName, StringComparison.Ordinal)
        && FormatVersion == ReactiveFormat.FormatVersion;
}

internal sealed class ReactiveSection
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("start")] public double Start { get; set; }
    [JsonPropertyName("end")] public double End { get; set; }
    [JsonPropertyName("mood")] public string Mood { get; set; } = "";
}

internal sealed class ReactiveTimelineEvent
{
    [JsonPropertyName("time")] public double Time { get; set; }
    [JsonPropertyName("target")] public string Target { get; set; } = "";
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("duration")] public double Duration { get; set; }
    [JsonPropertyName("easing")] public string Easing { get; set; } = "linear";
    [JsonPropertyName("params")] public Dictionary<string, object?> Params { get; set; } = [];
}

internal sealed class ReactiveAssetRef
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

internal sealed class ReactiveShaderPack
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("shader")] public string Shader { get; set; } = "";
    [JsonPropertyName("inputs")] public Dictionary<string, ReactiveShaderInput> Inputs { get; set; } = [];
}

internal sealed class ReactiveShaderInput
{
    [JsonPropertyName("type")] public string Type { get; set; } = "number";
    [JsonPropertyName("min")] public double? Min { get; set; }
    [JsonPropertyName("max")] public double? Max { get; set; }
    [JsonPropertyName("default")] public object? Default { get; set; }
}

internal sealed record ReactiveRuntimeState(
    ReactiveSection? CurrentSection,
    IReadOnlyList<ReactiveTimelineEvent> ActiveTransitions,
    IReadOnlyDictionary<string, object?> CurrentParams);
