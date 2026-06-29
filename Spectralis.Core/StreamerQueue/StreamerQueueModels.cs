namespace Spectralis.Core.StreamerQueue;

public enum SqTier { Normal, Skip, SuperSkip }

public enum SqStatus { Pending, Queued, Approved, AwaitingPayment, Playing, Played, Rejected, PaymentFailed }

public sealed record SqFeeSettings(bool Enabled, double Amount, string Currency);

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
    string SourceKind,
    SqTier Tier,
    string? TierChangedAtUtc,
    SqStatus Status,
    string? PaymentStatus,
    double? DurationSeconds,
    string SubmittedAtUtc,
    string? EditedAtUtc);

public sealed record SqRoom(
    string RoomId,
    bool Enabled,
    SqSettings Settings,
    string? ChannelId,
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

public sealed record SqStripeConnectResponse(string ConnectUrl);
