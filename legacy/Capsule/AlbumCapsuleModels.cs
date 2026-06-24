using System.Text.Json.Serialization;

namespace Spectralis;

internal static class AlbumCapsuleFormat
{
    public const string FormatName = "spectralis-album";
    public const int FormatVersion = 1;
    public const string DefaultAlgorithm = "Ed25519";
    public static readonly byte[] MagicBytes = [0x53, 0x50, 0x41, 0x43]; // SPAC
}

internal sealed class AlbumManifest
{
    [JsonPropertyName("format")] public string Format { get; set; } = AlbumCapsuleFormat.FormatName;
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = AlbumCapsuleFormat.FormatVersion;
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("artist")] public string Artist { get; set; } = "";
    [JsonPropertyName("release")] public CapsuleRelease Release { get; set; } = new();
    [JsonPropertyName("signature")] public CapsuleSignatureBlock Signature { get; set; } = new();
    [JsonPropertyName("capabilities")] public List<string> Capabilities { get; set; } = [];
    [JsonPropertyName("story")] public CapsuleStory Story { get; set; } = new();
    [JsonPropertyName("world")] public AlbumWorldSection? World { get; set; }
    [JsonPropertyName("tracks")] public List<AlbumTrackEntry> Tracks { get; set; } = [];
}

internal sealed class AlbumWorldSection
{
    [JsonPropertyName("entry")] public string Entry { get; set; } = "";
    [JsonPropertyName("binaryAssets")] public Dictionary<string, string> BinaryAssets { get; set; } = [];
    [JsonPropertyName("dataAssets")] public Dictionary<string, string> DataAssets { get; set; } = [];
}

internal sealed class AlbumTrackEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("artist")] public string Artist { get; set; } = "";
    [JsonPropertyName("audio")] public CapsuleAudio Audio { get; set; } = new();
    [JsonPropertyName("story")] public CapsuleStory Story { get; set; } = new();
    [JsonPropertyName("assets")] public CapsuleAssets Assets { get; set; } = new();
    [JsonPropertyName("visualizers")] public List<System.Text.Json.JsonElement> Visualizers { get; set; } = [];
    [JsonPropertyName("timeline")] public List<object> Timeline { get; set; } = [];
    [JsonPropertyName("suppressAppLyrics")] public bool SuppressAppLyrics { get; set; }
}
