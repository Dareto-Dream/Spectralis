using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis.Core.Capsule;

public static class CapsuleFormat
{
    public const string FormatName = "spectralis-capsule";
    public const int FormatVersion = 3;
    public const string DefaultAlgorithm = "Ed25519";
    public const string CdnKeyEndpointTemplate = "spectralis/keys/{0}.json";

    // Untrusted-input limits enforced before any payload is processed.
    public const long MaxCapsuleBytes = 768L * 1024 * 1024;
    public const long MaxEntryBytes = 512L * 1024 * 1024;
    public const long MaxManifestBytes = 8 * 1024 * 1024;
}

public sealed class CapsuleManifest
{
    [JsonPropertyName("format")] public string Format { get; set; } = "";
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; }
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

public sealed class CapsuleRelease
{
    [JsonPropertyName("album")] public string Album { get; set; } = "";
    [JsonPropertyName("year")] public int Year { get; set; }
    [JsonPropertyName("credits")] public List<string> Credits { get; set; } = [];
}

public sealed class CapsuleStory
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

public sealed class CapsuleAudio
{
    [JsonPropertyName("entry")] public string Entry { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("durationSeconds")] public double DurationSeconds { get; set; }
}

public sealed class CapsuleAssets
{
    [JsonPropertyName("images")] public List<string> Images { get; set; } = [];
    [JsonPropertyName("fonts")] public List<string> Fonts { get; set; } = [];
    [JsonPropertyName("videos")] public List<string> Videos { get; set; } = [];
    [JsonPropertyName("data")] public List<string> Data { get; set; } = [];
}

public sealed class CapsuleSignatureBlock
{
    [JsonPropertyName("keyId")] public string KeyId { get; set; } = "";
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonPropertyName("algorithm")] public string Algorithm { get; set; } = CapsuleFormat.DefaultAlgorithm;
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

public static class CapsuleHeader
{
    public static readonly byte[] MagicBytes = [0x53, 0x50, 0x43, 0x43]; // 'SPCC'
    public const int CurrentVersion = 3;
}

public static class CapsuleCapability
{
    public const string AppThemeDeepControl = "app.theme.deepControl";
    public const string AppLayoutDeepControl = "app.layout.deepControl";
    public const string AppChromeEffects = "app.chrome.effects";
    public const string VisualizerMultiLayer = "visualizer.multiLayer";
    public const string VisualizerWasm = "visualizer.wasm";
    public const string VisualizerShaderPack = "visualizer.shaderPack";
    public const string WebViewLocalContent = "webview.localContent";
    public const string WebViewNetworkAccess = "webview.networkAccess";
    public const string SharedPlayHostCapsule = "sharedPlay.hostCapsule";
    public const string SharedPlayPackageUpload = "sharedPlay.packageUpload";
    public const string TimelineAppControl = "timeline.appControl";
    public const string AlbumWorld = "album.world";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        AppThemeDeepControl, AppLayoutDeepControl, AppChromeEffects,
        VisualizerMultiLayer, VisualizerWasm, VisualizerShaderPack,
        WebViewLocalContent, WebViewNetworkAccess,
        SharedPlayHostCapsule, SharedPlayPackageUpload, TimelineAppControl,
        AlbumWorld,
    };
}

public sealed class CreatorKeyMetadata
{
    [JsonPropertyName("keyId")] public string KeyId { get; set; } = "";
    [JsonPropertyName("fingerprint")] public string Fingerprint { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("profileUrl")] public string? ProfileUrl { get; set; }
    [JsonPropertyName("avatarUrl")] public string? AvatarUrl { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "active";
    [JsonPropertyName("allowedCapabilities")] public List<string> AllowedCapabilities { get; set; } = [];
    [JsonPropertyName("createdAtUtc")] public DateTimeOffset CreatedAtUtc { get; set; }
    [JsonPropertyName("updatedAtUtc")] public DateTimeOffset UpdatedAtUtc { get; set; }
    [JsonPropertyName("revokedAtUtc")] public DateTimeOffset? RevokedAtUtc { get; set; }

    [JsonIgnore]
    public bool IsActive =>
        string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase) && RevokedAtUtc is null;
}

/// <summary>
/// Context passed to the trust prompt when a capsule creator is not yet trusted.
/// Bundles the creator identity with the capabilities and content tags declared in
/// the manifest so the UI can render a complete disclosure dialog.
/// </summary>
public sealed class CapsuleTrustContext
{
    public required CreatorKeyMetadata Creator { get; init; }
    public IReadOnlyList<string> RequestedCapabilities { get; init; } = [];
    public IReadOnlyList<string> ContentTags { get; init; } = [];
}

public sealed record CreatorTrustEntry(
    string Fingerprint,
    string DisplayName,
    DateTimeOffset AllowedAtUtc,
    DateTimeOffset? LastValidatedUtc,
    DateTimeOffset? MetadataCachedAtUtc);
