using System.Text.Json;

namespace Spectralis.Core.SharedPlay;

// ─── Collaborative rooms (WebSocket) ───────────────────────────────────────────

/// <summary>A participant's authority in a collaborative room.</summary>
public enum SharedPlayRole
{
    Follower,
    CoDj,
    Host,
}

public static class SharedPlayRoleExtensions
{
    public static string ToWire(this SharedPlayRole role) => role switch
    {
        SharedPlayRole.Host => "host",
        SharedPlayRole.CoDj => "codj",
        _ => "follower",
    };

    public static SharedPlayRole ParseRole(string? wire) => wire switch
    {
        "host" => SharedPlayRole.Host,
        "codj" => SharedPlayRole.CoDj,
        _ => SharedPlayRole.Follower,
    };
}

/// <summary>Room-wide capability toggles. Each *_Cap value is a minimum role
/// name ("everyone" | "codj" | "host").</summary>
public sealed record SharedPlayCapabilities(
    string QueueAdd,
    string QueueRemove,
    string QueueReorder,
    string Transport,
    bool VoteSkip,
    int SkipVotesRequired)
{
    public static readonly SharedPlayCapabilities Default =
        new("everyone", "host", "host", "host", true, 3);

    public static SharedPlayCapabilities FromJson(JsonElement el)
    {
        string Cap(string name, string fallback) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? fallback
                : fallback;
        var d = Default;
        return new SharedPlayCapabilities(
            Cap("queueAdd", d.QueueAdd),
            Cap("queueRemove", d.QueueRemove),
            Cap("queueReorder", d.QueueReorder),
            Cap("transport", d.Transport),
            el.TryGetProperty("voteSkip", out var vs) && vs.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? vs.GetBoolean() : d.VoteSkip,
            el.TryGetProperty("skipVotesRequired", out var sv) && sv.TryGetInt32(out var n)
                ? Math.Clamp(n, 1, 20) : d.SkipVotesRequired);
    }
}

public sealed record SharedPlayMember(
    string ClientId,
    string Name,
    SharedPlayRole Role,
    bool IsHost);

public sealed record SharedPlaySkipProgress(int Votes, int Required);

/// <summary>A permitted listener command relayed to the host to apply to the engine.</summary>
public sealed record SharedPlayIncomingCommand(
    string Cmd,
    string ByName,
    string ByClientId,
    JsonElement Payload);

public sealed record SharedPlayTrackDescriptor(
    string DisplayName,
    string? Artist,
    string? Album,
    double DurationSeconds,
    string FormatName,
    int Channels,
    int SourceSampleRate,
    int BitsPerSample,
    bool HasAlbumArt,
    bool HasLyrics,
    bool HasEmbeddedVisualizer,
    bool HasEmbeddedTheme,
    bool HasEmbeddedContent,
    SharedPlayLyricLine[] Lyrics);

public sealed record SharedPlayLyricLine(
    double TimeSeconds,
    string Text);

public sealed record SharedPlayPackage(
    string TrackId,
    string PackagePath,
    string AudioSha256,
    string PackageSha256,
    long AudioBytes,
    long PackageBytes,
    string AudioExtension,
    SharedPlayTrackDescriptor Track,
    DateTimeOffset CreatedAtUtc);

public sealed record SharedPlayPlaybackSnapshot(
    bool IsPlaying,
    double PositionSeconds,
    double DurationSeconds,
    string Reason,
    DateTimeOffset HostClockUtc,
    string? TrackId = null);

public sealed record SharedPlaySessionSnapshot(
    bool IsEnabled,
    bool IsUploading,
    string? RoomCode,
    string? DisplayCode,
    string? JoinUrl,
    string? TrackId,
    string? ChannelUrl,
    string? LastError);

/// <summary>A session fetched by a listener joining someone else's Shared Play room —
/// the receiver-side counterpart to <see cref="SharedPlayRoomSession"/> (which is host-only).</summary>
public sealed record SharedPlayJoinedSession(
    string RoomCode,
    string? TrackId,
    Uri StateUrl,
    Uri QueueUrl,
    Uri PackageUrl,
    DateTimeOffset? ExpiresAtUtc);

public sealed record SharedPlayRoomSession(
    string RoomCode,
    string DisplayCode,
    string JoinUrl,
    string TrackId,
    Uri StateUrl,
    Uri QueueUrl,
    DateTimeOffset? ExpiresAtUtc,
    string SessionKey = "",
    Uri? PresenceUrl = null,
    Uri? ReactionsUrl = null);

public sealed record SharedPlayPresenceSnapshot(
    int ListenerCount,
    DateTimeOffset UpdatedAtUtc);

public sealed record SharedPlayReactionItem(
    string Id,
    string Type,
    string Label,
    DateTimeOffset CreatedAtUtc);

public sealed record SharedPlayReactionsSnapshot(
    SharedPlayReactionItem[] Items,
    DateTimeOffset UpdatedAtUtc);

public sealed record SharedPlayCreateSessionRequest(
    string ProtocolVersion,
    string ClientName,
    SharedPlayTrackDescriptor Track,
    SharedPlayPackageDescriptor Package,
    SharedPlayPlaybackSnapshot Playback,
    SharedPlayCapabilityDescriptor Capabilities);

public sealed record SharedPlayPackageDescriptor(
    string TrackId,
    string AudioSha256,
    string PackageSha256,
    long AudioBytes,
    long PackageBytes,
    string AudioExtension,
    string ContentType);

public sealed record SharedPlayCapabilityDescriptor(
    bool SpectralisRichPackage,
    bool PreservesEmbeddedMetadata,
    bool PreservesAlbumArt,
    bool PreservesEmbeddedVisualizer,
    bool BrowserFallbackIncluded);

public sealed class SharedPlayCreateSessionResponse
{
    public string? RoomCode { get; init; }
    public string? DisplayCode { get; init; }
    public string? SessionKey { get; init; }
    public string? TrackId { get; init; }
    public string? JoinUrl { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public string? StateUrl { get; init; }
    public string? QueueUrl { get; init; }
    public SharedPlayUploadTarget[]? Uploads { get; init; }
}

public sealed class SharedPlayUploadTarget
{
    public string? Name { get; init; }
    public string? Method { get; init; }
    public string? UploadUrl { get; init; }
    public string? AssetUrl { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
}

public sealed record SharedPlayPreparedTrack(
    string FileKey,
    string TrackId,
    Uri PackageUrl,
    SharedPlayTrackDescriptor Track,
    DateTimeOffset PreparedAtUtc);

public sealed record SharedPlayQueueSnapshot(
    int Version,
    int CurrentIndex,
    SharedPlayQueueItem[] Items,
    DateTimeOffset UpdatedAtUtc);

public sealed record SharedPlayQueueItem(
    string Id,
    string SourceKind,
    string Title,
    string? Artist,
    string? Album,
    string? Url,
    string? SourceId,
    double? DurationSeconds,
    string AddedBy,
    DateTimeOffset AddedAtUtc,
    string? TrackId = null,
    Uri? PackageUrl = null);

public sealed record SharedPlayChannelPublishRequest(
    string ProtocolVersion,
    string OwnerToken,
    string DisplayName,
    bool IsLive,
    string? RoomCode,
    string? JoinUrl,
    string? TrackId,
    SharedPlayTrackDescriptor? Track,
    SharedPlayPlaybackSnapshot? Playback);

public sealed class SharedPlayChannelResponse
{
    public string? ChannelId { get; init; }
    public string? ChannelUrl { get; init; }
    public bool IsLive { get; init; }
    public string? RoomCode { get; init; }
    public string? JoinUrl { get; init; }
}

// ─── Streamer Queue ────────────────────────────────────────────────────────────

public sealed class StreamerQueueState
{
    public string RoomCode { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public string? ChannelId { get; init; }
    public StreamerQueueSettings Settings { get; init; } = new();
    public List<StreamerQueueSubmission> Submissions { get; init; } = [];
    public List<StreamerQueueSkipRequest> SkipRequests { get; init; } = [];
    public List<StreamerQueueSkipRequest> SuperSkipRequests { get; init; } = [];
    public string? StripePublishableKey { get; init; }
    public bool StripeConnected { get; init; }
}

public sealed class StreamerQueueSettings
{
    public bool RequireApproval { get; init; }
    public int MaxQueueLength { get; init; } = 50;
    public bool AllowDuplicates { get; init; }
    public StreamerQueueFeeSettings QueueEntryFee { get; init; } = new();
    public StreamerQueueSkipFeeSettings SkipRequests { get; init; } = new();
    public StreamerQueueFeeSettings SuperSkips { get; init; } = new();
}

public sealed class StreamerQueueFeeSettings
{
    public bool Enabled { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";
}

public sealed class StreamerQueueSkipFeeSettings
{
    public bool Enabled { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";
    public int VotesRequired { get; init; } = 3;
}

public sealed class StreamerQueueSubmission
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string Status { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public DateTimeOffset SubmittedAtUtc { get; init; }
}

public sealed class StreamerQueueSkipRequest
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string PaymentStatus { get; init; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; init; }
}

public sealed class StreamerQueuePutRequest
{
    public string? SessionKey { get; init; }
    public bool Enabled { get; init; }
    public StreamerQueueSettings? Settings { get; init; }
}

public sealed class StreamerQueueStripeConnectResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("connectUrl")]
    public string? ConnectUrl { get; init; }
}
