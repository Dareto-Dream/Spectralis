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
    string? SessionId,
    string? JoinUrl,
    string? TrackId,
    string? ChannelUrl,
    string? LastError);

public sealed record SharedPlayUploadRequest(
    string ProtocolVersion,
    string ClientName,
    string PackageKind,
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

public sealed class SharedPlayUploadResponse
{
    public string? SessionId { get; init; }
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

public sealed class SharedPlaySession
{
    public required string SessionId { get; init; }
    public required string JoinUrl { get; init; }
    public required string TrackId { get; init; }
    public required Uri StateUrl { get; init; }
    public required Uri QueueUrl { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed record SharedPlayPreparedTrack(
    string FileKey,
    string TrackId,
    Uri PackageUrl,
    SharedPlayTrackDescriptor Track,
    DateTimeOffset PreparedAtUtc);

public sealed record SharedPlayRemoteSession(
    string SessionId,
    string? TrackId,
    Uri StateUrl,
    Uri QueueUrl,
    Uri PackageUrl,
    DateTimeOffset? ExpiresAtUtc);

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
    string? SessionId,
    string? JoinUrl,
    string? TrackId,
    SharedPlayTrackDescriptor? Track,
    SharedPlayPlaybackSnapshot? Playback);

public sealed class SharedPlayChannelResponse
{
    public string? ChannelId { get; init; }
    public string? ChannelUrl { get; init; }
    public bool IsLive { get; init; }
    public string? SessionId { get; init; }
    public string? JoinUrl { get; init; }
}
