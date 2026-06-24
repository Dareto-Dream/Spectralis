using System.Text.Json.Serialization;

namespace Spectralis;

internal sealed class CreatorKeyMetadata
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

    [JsonIgnore] public bool IsActive => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase) && RevokedAtUtc is null;
}

internal sealed record CreatorTrustEntry(
    string Fingerprint,
    string DisplayName,
    DateTimeOffset AllowedAtUtc,
    DateTimeOffset? LastValidatedUtc,
    DateTimeOffset? MetadataCachedAtUtc);
