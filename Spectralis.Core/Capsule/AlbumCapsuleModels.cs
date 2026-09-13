using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis.Core.Capsule;

public static class AlbumCapsuleFormat
{
    public const string FormatName = "spectralis-album";
    public const int FormatVersion = 1;
    public static readonly byte[] MagicBytes = [0x53, 0x50, 0x41, 0x43]; // SPAC
}

public sealed class AlbumManifest
{
    [JsonPropertyName("format")] public string Format { get; set; } = AlbumCapsuleFormat.FormatName;
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = AlbumCapsuleFormat.FormatVersion;
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("artist")] public string Artist { get; set; } = string.Empty;
    [JsonPropertyName("release")] public CapsuleRelease Release { get; set; } = new();
    [JsonPropertyName("signature")] public CapsuleSignatureBlock Signature { get; set; } = new();
    [JsonPropertyName("capabilities")] public List<string> Capabilities { get; set; } = [];
    [JsonPropertyName("story")] public CapsuleStory Story { get; set; } = new();
    [JsonPropertyName("world")] public AlbumWorldSection? World { get; set; }
    [JsonPropertyName("tracks")] public List<AlbumTrackEntry> Tracks { get; set; } = [];
}

public sealed class AlbumWorldSection
{
    [JsonPropertyName("entry")] public string Entry { get; set; } = string.Empty;
    [JsonPropertyName("binaryAssets")] public Dictionary<string, string> BinaryAssets { get; set; } = [];
    [JsonPropertyName("dataAssets")] public Dictionary<string, string> DataAssets { get; set; } = [];

    /// <summary>
    /// Package-relative path to a sandboxed Wasm/wgpu world module (Phase 2 dual-runtime
    /// rework — see docs/formats/spectral-album-world.md). Optional; empty means this world
    /// has no Wasm runtime, only the HTML one (<see cref="Entry"/>) — the common case today.
    /// A world may declare both: the HTML surface can request a hand-off into the Wasm one via
    /// <c>spectral.worlds.switchToWasm()</c>, and vice versa via the Wasm module's
    /// <c>switch_to_html</c> host import. Requires the <c>worlds.wasm3d</c> capability.
    /// </summary>
    [JsonPropertyName("wasmEntry")] public string WasmEntry { get; set; } = string.Empty;
}

public sealed class AlbumTrackEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("artist")] public string Artist { get; set; } = string.Empty;
    [JsonPropertyName("audio")] public CapsuleAudio Audio { get; set; } = new();
    [JsonPropertyName("story")] public CapsuleStory Story { get; set; } = new();
    [JsonPropertyName("assets")] public CapsuleAssets Assets { get; set; } = new();
    [JsonPropertyName("visualizers")] public List<JsonElement> Visualizers { get; set; } = [];
    [JsonPropertyName("timeline")] public List<object> Timeline { get; set; } = [];
    [JsonPropertyName("suppressAppLyrics")] public bool SuppressAppLyrics { get; set; }
}
