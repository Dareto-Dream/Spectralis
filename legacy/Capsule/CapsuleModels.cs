using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis;

internal static class CapsuleFormat
{
    public const string FormatName = "spectralis-capsule";
    public const int FormatVersion = 3;
    public const string DefaultAlgorithm = "Ed25519";
    public const string CdnKeyEndpointTemplate = "spectralis/keys/{0}.json";
}

internal sealed class CapsuleManifest
{
    [JsonPropertyName("format")] public string Format { get; set; } = CapsuleFormat.FormatName;
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = CapsuleFormat.FormatVersion;
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("artist")] public string Artist { get; set; } = "";
    [JsonPropertyName("release")] public CapsuleRelease Release { get; set; } = new();
    [JsonPropertyName("story")] public CapsuleStory Story { get; set; } = new();
    [JsonPropertyName("audio")] public CapsuleAudio Audio { get; set; } = new();
    [JsonPropertyName("assets")] public CapsuleAssets Assets { get; set; } = new();
    [JsonPropertyName("visualizers")] public List<object> Visualizers { get; set; } = [];
    [JsonPropertyName("timeline")] public List<object> Timeline { get; set; } = [];
    [JsonPropertyName("suppressAppLyrics")] public bool SuppressAppLyrics { get; set; }
    [JsonPropertyName("capabilities")] public List<string> Capabilities { get; set; } = [];
    [JsonPropertyName("signature")] public CapsuleSignatureBlock Signature { get; set; } = new();
}

internal sealed class CapsuleRelease
{
    [JsonPropertyName("album")] public string Album { get; set; } = "";
    [JsonPropertyName("year")] public int Year { get; set; }
    [JsonPropertyName("credits")] public List<string> Credits { get; set; } = [];
}

internal sealed class CapsuleStory
{
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("backstory")] public string Backstory { get; set; } = "";
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = [];
    [JsonPropertyName("presentation")] public string Presentation { get; set; } = "";
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
    [JsonPropertyName("image")] public string Image { get; set; } = "";
    [JsonPropertyName("imageEntry")] public string ImageEntry { get; set; } = "";
    [JsonPropertyName("explainerImage")] public string ExplainerImage { get; set; } = "";
    [JsonPropertyName("characterImage")] public string CharacterImage { get; set; } = "";
    [JsonPropertyName("chapters")] public List<JsonElement> Chapters { get; set; } = [];
    [JsonPropertyName("pages")] public List<JsonElement> Pages { get; set; } = [];
}

internal sealed class CapsuleStoryPage
{
    [JsonPropertyName("speaker")] public string Speaker { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("image")] public string Image { get; set; } = "";
    [JsonPropertyName("imageEntry")] public string ImageEntry { get; set; } = "";
    [JsonPropertyName("explainerImage")] public string ExplainerImage { get; set; } = "";
    [JsonPropertyName("characterImage")] public string CharacterImage { get; set; } = "";
    [JsonPropertyName("portrait")] public string Portrait { get; set; } = "";
    [JsonPropertyName("sprite")] public string Sprite { get; set; } = "";
}

internal sealed record CapsuleStoryScene(
    string Speaker,
    string Text,
    byte[]? ImageBytes,
    string? ImageName);

internal sealed record CapsuleStoryDocument(
    IReadOnlyList<CapsuleStoryScene> Scenes);

internal sealed class CapsuleAudio
{
    [JsonPropertyName("entry")] public string Entry { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("durationSeconds")] public double DurationSeconds { get; set; }
}

internal sealed class CapsuleAssets
{
    [JsonPropertyName("images")] public List<string> Images { get; set; } = [];
    [JsonPropertyName("fonts")] public List<string> Fonts { get; set; } = [];
    [JsonPropertyName("videos")] public List<string> Videos { get; set; } = [];
    [JsonPropertyName("data")] public List<string> Data { get; set; } = [];
}

internal sealed class CapsuleSignatureBlock
{
    [JsonPropertyName("keyId")] public string KeyId { get; set; } = "";
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonPropertyName("algorithm")] public string Algorithm { get; set; } = CapsuleFormat.DefaultAlgorithm;
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

internal sealed class CapsuleHeader
{
    public static readonly byte[] MagicBytes = [0x53, 0x50, 0x43, 0x43]; // 'SPCC'
    public const int CurrentVersion = 3;

    public int Version { get; set; } = CurrentVersion;
    public string KeyFingerprint { get; set; } = "";
    public long SignatureOffset { get; set; }
    public long PayloadOffset { get; set; }
}
