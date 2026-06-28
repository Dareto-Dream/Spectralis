namespace Spectralis.Core.SharedPlay;

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

public sealed record SharedPlayRoomSession(
    string RoomCode,
    string DisplayCode,
    string JoinUrl,
    string TrackId,
    Uri StateUrl,
    Uri QueueUrl,
    DateTimeOffset? ExpiresAtUtc,
    string SessionKey = "");

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
