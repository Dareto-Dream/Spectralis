namespace Spectralis.Core.StreamerQueue;

public enum SqTier { Normal, Skip, SuperSkip }

public enum SqStatus { Pending, Queued, Approved, AwaitingPayment, Playing, Played, Skipped, Rejected, PaymentFailed }

public sealed record SqFeeSettings(bool Enabled, double Amount, string Currency);

/// <summary>A named priority lane a streamer sends out as its own share link (e.g.
/// "General", "VIP"), optionally paired to a Discord channel so a bot posting there
/// routes into this lane. Orthogonal to <see cref="SqTier"/> — a paid skip still jumps
/// every channel; channels only reorder submissions within the same paid tier.</summary>
public sealed record SqQueueChannel(string Id, string Name, int Priority, string? DiscordChannelId);

/// <summary>One slot in a repeating mix cycle (e.g. 2×General, 3×VIP, repeat) used to
/// interleave channels instead of strict priority ordering.</summary>
public sealed record SqMixSlot(string ChannelId, int Count);

public sealed record SqSettings(
    bool RequireApproval,
    bool AllowDuplicates,
    bool AllowLinkSubmissions,
    int MaxQueueLength,
    int MaxSubmissionsPerPerson,
    bool SkipBypassesLimit,
    SqFeeSettings QueueEntryFee,
    SqFeeSettings Skip,
    SqFeeSettings SuperSkip);

public sealed record SqSubmission(
    string Id,
    string DisplayName,
    string? Title,
    string? Artist,
    string? Url,
    string? FileId,
    string? FileName,
    string SourceKind,
    SqTier Tier,
    string? TierChangedAtUtc,
    string? QueueChannelId,
    SqStatus Status,
    string? PaymentStatus,
    double? DurationSeconds,
    string SubmittedAtUtc,
    string? EditedAtUtc);

public sealed record SqRoom(
    string RoomId,
    bool Enabled,
    bool AcceptingSubmissions,
    SqSettings Settings,
    string? ChannelId,
    SqQueueChannel[]? QueueChannels,
    SqMixSlot[]? ChannelMixPattern,
    string? StripePublishableKey,
    string? NowPlayingId,
    string? NowPlayingTier,
    string[]? ManualOrderIds,
    SqSubmission[] Submissions,
    SqSubmission[] OrderedQueue,
    string CreatedAtUtc,
    string UpdatedAtUtc);

public sealed record SqSubmitResponse(
    string SubmissionId,
    string Status,
    int? Position,
    double? WaitEstimateLowMins,
    double? WaitEstimateHighMins,
    string? ClientSecret);

public sealed record SqPromoteResponse(
    string SubmissionId,
    string Tier,
    int? Position,
    double? WaitEstimateLowMins,
    double? WaitEstimateHighMins,
    string? ClientSecret);

public sealed record SqCreateRoomResponse(string RoomId, string OwnerToken);

public sealed record SqAddTrackResponse(string SubmissionId, string Status);

public sealed record SqStripeConnectResponse(string ConnectUrl);

public sealed record SqDiscordPinResponse(string Pin, string ExpiresAtUtc);
