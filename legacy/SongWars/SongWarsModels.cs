namespace Spectralis;

internal enum SongWarsTournamentMode
{
    Judges
}

internal enum SongWarsSubmissionStatus
{
    Active,
    LosersBracket,
    Eliminated,
    Withdrawn
}

internal enum SongWarsBracket
{
    Winners,
    Losers,
    GrandFinals
}

internal enum SongWarsMatchPhase
{
    Ready,
    Pending,
    TrackAPlaying,
    TrackBPlaying,
    PrimaryVoting,
    EliminationVoting,
    Reveal,
    Complete,
    Skipped,
    Paused
}

internal enum SongWarsMatchSlot
{
    A,
    B
}

internal enum SongWarsVotePhase
{
    Primary,
    Elimination
}

internal enum SongWarsVoteChoice
{
    Pass,
    Fail,
    Eliminated
}

internal enum SongWarsTrackVoteChoice
{
    Pass,
    Fail
}

internal enum SongWarsOutcome
{
    Pending,
    Pass,
    Fail,
    Eliminated,
    Skip
}

internal sealed class SongWarsTournament
{
    public string TournamentId { get; set; } = "";
    public string Name { get; set; } = "";
    public SongWarsTournamentMode Mode { get; set; } = SongWarsTournamentMode.Judges;
    public List<SongWarsSubmission> Submissions { get; set; } = [];
    public List<SongWarsJudge> Judges { get; set; } = [];
    public List<SongWarsMatch> Matches { get; set; } = [];
    public List<string> MatchOrder { get; set; } = [];
    public string? CurrentMatchId { get; set; }
    public int EliminationCount { get; set; }
    public bool LastMatchDirectEliminated { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<SongWarsAuditEntry> AuditLog { get; set; } = [];
}

internal sealed class SongWarsSubmission
{
    public string SubmissionId { get; set; } = "";
    public string DisplayTitle { get; set; } = "";
    public string ArtistDisplayName { get; set; } = "";
    public int? Seed { get; set; }
    public int Losses { get; set; }
    public string? LocalFilePath { get; set; }
    public string? ExternalSource { get; set; }
    public double? DurationSeconds { get; set; }
    public SongWarsSubmissionStatus Status { get; set; } = SongWarsSubmissionStatus.Active;
}

internal sealed class SongWarsJudge
{
    public string JudgeId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string JoinToken { get; set; } = "";
    public bool SharedPlayOptIn { get; set; }
    public DateTimeOffset? LastSeenUtc { get; set; }
}

internal sealed class SongWarsMatch
{
    public string MatchId { get; set; } = "";
    public SongWarsBracket Bracket { get; set; }
    public int RoundIndex { get; set; }
    public string RoundId { get; set; } = "";
    public string SlotASubmissionId { get; set; } = "";
    public string SlotBSubmissionId { get; set; } = "";
    public SongWarsMatchSlot FocusSlot { get; set; } = SongWarsMatchSlot.B;
    public SongWarsMatchPhase Phase { get; set; } = SongWarsMatchPhase.Pending;
    public SongWarsMatchPhase? PausedFromPhase { get; set; }
    public string? WinnerSubmissionId { get; set; }
    public string? LoserSubmissionId { get; set; }
    public SongWarsOutcome Result { get; set; } = SongWarsOutcome.Pending;
    public List<SongWarsVoteSnapshot> VoteSnapshots { get; set; } = [];
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public bool WasDirectElimination { get; set; }
    public string? ReplayedFromMatchId { get; set; }
}

internal sealed class SongWarsVote
{
    public string JudgeId { get; set; } = "";
    public string MatchId { get; set; } = "";
    public SongWarsVotePhase Phase { get; set; } = SongWarsVotePhase.Primary;
    public SongWarsVoteChoice Choice { get; set; }
    public SongWarsTrackVoteChoice? SlotAChoice { get; set; }
    public SongWarsTrackVoteChoice? SlotBChoice { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int Revision { get; set; } = 1;
}

internal sealed class SongWarsVoteSnapshot
{
    public SongWarsVotePhase Phase { get; set; }
    public SongWarsOutcome Outcome { get; set; }
    public int PassCount { get; set; }
    public int FailCount { get; set; }
    public int EliminatedCount { get; set; }
    public int TrackAPassCount { get; set; }
    public int TrackAFailCount { get; set; }
    public int TrackBPassCount { get; set; }
    public int TrackBFailCount { get; set; }
    public SongWarsMatchSlot? EliminatedSlot { get; set; }
    public string RuleApplied { get; set; } = "";
    public string Explanation { get; set; } = "";
    public DateTimeOffset RevealedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class SongWarsAuditEntry
{
    public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Kind { get; set; } = "";
    public string Message { get; set; } = "";
    public string? MatchId { get; set; }
}

internal sealed record SongWarsRoundDescriptor(
    SongWarsBracket Bracket,
    int RoundIndex,
    string RoundId);

internal sealed record SongWarsTrackPointer(
    string SubmissionId,
    string DisplayTitle,
    string ArtistDisplayName,
    string? LocalFilePath,
    string? ExternalSource,
    double? DurationSeconds);
