namespace Spectralis;

internal static class SongWarsBracketEngine
{
    public const int MaxSubmissionCount = 32;
    public const int MinSubmissionCount = 2;

    public static IReadOnlyList<SongWarsRoundDescriptor> PlannedRoundOrder { get; } =
    [
        new(SongWarsBracket.Winners, 1, "winners-r1"),
        new(SongWarsBracket.Losers, 1, "losers-r1"),
        new(SongWarsBracket.Winners, 2, "winners-r2"),
        new(SongWarsBracket.Losers, 2, "losers-r2"),
        new(SongWarsBracket.Winners, 3, "winners-r3"),
        new(SongWarsBracket.Losers, 3, "losers-r3"),
        new(SongWarsBracket.Winners, 4, "winners-semis"),
        new(SongWarsBracket.Losers, 4, "losers-semis"),
        new(SongWarsBracket.Winners, 5, "winners-finals"),
        new(SongWarsBracket.Losers, 5, "losers-finals"),
        new(SongWarsBracket.GrandFinals, 1, "grand-finals")
    ];

    public static SongWarsTournament CreateTournament(
        string name,
        IReadOnlyList<SongWarsSubmission> submissions,
        IReadOnlyList<SongWarsJudge> judges,
        int? randomSeed = null)
    {
        if (submissions.Count < MinSubmissionCount || submissions.Count > MaxSubmissionCount)
            throw new ArgumentException($"Song Wars requires between {MinSubmissionCount} and {MaxSubmissionCount} submissions.", nameof(submissions));

        if (judges.Count is < 1 or > 5)
            throw new ArgumentException("Song Wars v1 requires 1-5 judges.", nameof(judges));

        var now = DateTimeOffset.UtcNow;
        var normalizedSubmissions = NormalizeSubmissions(submissions, randomSeed);
        var normalizedJudges = NormalizeJudges(judges);

        var tournament = new SongWarsTournament
        {
            TournamentId = NewId("sw"),
            Name = string.IsNullOrWhiteSpace(name) ? "Song Wars" : name.Trim(),
            Mode = SongWarsTournamentMode.Judges,
            Submissions = normalizedSubmissions,
            Judges = normalizedJudges,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        // Seed the initial Winners Round 1. Top seed gets a bye when count is odd.
        var sorted = normalizedSubmissions.OrderBy(s => s.Seed ?? int.MaxValue).ToList();
        var startIndex = sorted.Count % 2 == 0 ? 0 : 1;
        if (startIndex == 1)
            AppendAudit(tournament, "round-bye", $"{sorted[0].DisplayTitle} received a winners-r1 bye.");
        for (var i = startIndex; i + 1 < sorted.Count; i += 2)
            AddMatch(tournament, SongWarsBracket.Winners, 1, "winners-r1", sorted[i].SubmissionId, sorted[i + 1].SubmissionId);

        SetCurrentToNextPending(tournament);
        AppendAudit(tournament, "created", $"Created {tournament.Name} with {submissions.Count} submission(s) and {judges.Count} judge(s).");
        return tournament;
    }

    public static void StartTournament(SongWarsTournament tournament)
    {
        var current = GetCurrentMatch(tournament);
        if (current is null)
        {
            SetCurrentToNextPending(tournament);
            current = GetCurrentMatch(tournament);
        }

        if (current is null)
            throw new InvalidOperationException("Song Wars could not find a match to start.");

        if (current.Phase == SongWarsMatchPhase.Pending)
            current.Phase = SongWarsMatchPhase.Ready;

        Touch(tournament);
        AppendAudit(tournament, "started", "Tournament started.", current.MatchId);
    }

    public static SongWarsMatch? GetCurrentMatch(SongWarsTournament tournament) =>
        string.IsNullOrWhiteSpace(tournament.CurrentMatchId)
            ? null
            : tournament.Matches.FirstOrDefault(m => string.Equals(
                m.MatchId,
                tournament.CurrentMatchId,
                StringComparison.OrdinalIgnoreCase));

    public static SongWarsMatch AdvanceMatch(
        SongWarsTournament tournament,
        string matchId,
        SongWarsOutcome outcome,
        SongWarsVoteSnapshot? voteSnapshot = null)
    {
        var match = tournament.Matches.FirstOrDefault(m => string.Equals(
            m.MatchId,
            matchId,
            StringComparison.OrdinalIgnoreCase));
        if (match is null)
            throw new InvalidOperationException("Song Wars could not find the requested match.");

        if (match.Result != SongWarsOutcome.Pending)
            throw new InvalidOperationException("Song Wars cannot advance a match that already has a result.");

        if (voteSnapshot is not null)
            match.VoteSnapshots.Add(voteSnapshot);

        if (outcome == SongWarsOutcome.Pending)
            throw new InvalidOperationException("Song Wars cannot advance a match with a pending outcome.");

        if (outcome == SongWarsOutcome.Skip)
        {
            match.Result = SongWarsOutcome.Skip;
            match.Phase = SongWarsMatchPhase.Skipped;
            match.CompletedAtUtc = DateTimeOffset.UtcNow;
            RequeueSkippedMatch(tournament, match);
            AppendAudit(tournament, "match-skipped", "Match skipped and requeued at the end of its bracket round.", match.MatchId);
            SetCurrentToNextPending(tournament);
            Touch(tournament);
            return match;
        }

        var (winnerId, loserId, directElimination) = ResolveResult(match, outcome);
        match.WinnerSubmissionId = winnerId;
        match.LoserSubmissionId = loserId;
        match.Result = outcome;
        match.WasDirectElimination = directElimination;
        match.Phase = SongWarsMatchPhase.Complete;
        match.CompletedAtUtc = DateTimeOffset.UtcNow;

        ApplySubmissionResult(tournament, match, winnerId, loserId, directElimination);
        tournament.LastMatchDirectEliminated = directElimination;

        AppendAudit(
            tournament,
            "match-complete",
            $"{match.RoundId} completed with {outcome}.",
            match.MatchId);

        EnsureNextRound(tournament);
        SetCurrentToNextPending(tournament);
        Touch(tournament);
        return match;
    }

    internal static List<int> GenerateSeedOrder(int count)
    {
        if (count < 2 || (count & (count - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Seed count must be a power of two.");

        var order = new List<int> { 1, 2 };
        while (order.Count < count)
        {
            var nextSize = order.Count * 2;
            var next = new List<int>(nextSize);
            foreach (var seed in order)
            {
                next.Add(seed);
                next.Add(nextSize + 1 - seed);
            }

            order = next;
        }

        return order;
    }

    private static void EnsureNextRound(SongWarsTournament tournament)
    {
        if (tournament.Matches.Any(IsOpenMatch))
            return;

        var active = OrderedActiveSubmissions(tournament).ToList();
        var zeroLoss = active.Where(s => s.Losses == 0).ToList();
        var oneLoss = active.Where(s => s.Losses == 1).ToList();
        var lastCompleted = tournament.Matches
            .Where(m => m.Result is not SongWarsOutcome.Pending and not SongWarsOutcome.Skip)
            .MaxBy(m => m.CompletedAtUtc);

        if (active.Count == 1)
        {
            tournament.CurrentMatchId = null;
            AppendAudit(tournament, "complete", $"{active[0].DisplayTitle} won Song Wars.");
            return;
        }

        if (zeroLoss.Count == 1 && oneLoss.Count == 1)
        {
            AddGrandFinalsMatch(tournament, zeroLoss[0], oneLoss[0], reset: false);
            return;
        }

        if (zeroLoss.Count == 0 && oneLoss.Count == 2)
        {
            AddGrandFinalsMatch(tournament, oneLoss[0], oneLoss[1], reset: true);
            return;
        }

        if (oneLoss.Count > 1 && lastCompleted?.Bracket == SongWarsBracket.Winners)
        {
            AddRoundMatches(tournament, SongWarsBracket.Losers, oneLoss);
            return;
        }

        if (zeroLoss.Count > 1)
        {
            AddRoundMatches(tournament, SongWarsBracket.Winners, zeroLoss);
            return;
        }

        if (oneLoss.Count > 1)
            AddRoundMatches(tournament, SongWarsBracket.Losers, oneLoss);
    }

    private static void AddRoundMatches(
        SongWarsTournament tournament,
        SongWarsBracket bracket,
        IReadOnlyList<SongWarsSubmission> participants)
    {
        if (participants.Count < 2)
            return;

        var roundIndex = NextRoundIndex(tournament, bracket);
        var roundId = BuildRoundId(bracket, roundIndex);
        var startIndex = participants.Count % 2 == 0 ? 0 : 1;
        if (startIndex == 1)
        {
            AppendAudit(
                tournament,
                "round-bye",
                $"{participants[0].DisplayTitle} received a {roundId} bye.");
        }

        for (var i = startIndex; i + 1 < participants.Count; i += 2)
            AddMatch(tournament, bracket, roundIndex, roundId, participants[i].SubmissionId, participants[i + 1].SubmissionId);
    }

    private static void AddGrandFinalsMatch(
        SongWarsTournament tournament,
        SongWarsSubmission slotA,
        SongWarsSubmission slotB,
        bool reset)
    {
        var existingGrandFinals = tournament.Matches.Count(m => m.Bracket == SongWarsBracket.GrandFinals);
        var roundIndex = existingGrandFinals + 1;
        var roundId = reset ? "grand-finals-reset" : "grand-finals";
        AddMatch(tournament, SongWarsBracket.GrandFinals, roundIndex, roundId, slotA.SubmissionId, slotB.SubmissionId);
    }

    private static SongWarsMatch AddMatch(
        SongWarsTournament tournament,
        SongWarsBracket bracket,
        int roundIndex,
        string roundId,
        string slotAId,
        string slotBId)
    {
        var match = new SongWarsMatch
        {
            MatchId = NewId("match"),
            Bracket = bracket,
            RoundIndex = roundIndex,
            RoundId = roundId,
            SlotASubmissionId = slotAId,
            SlotBSubmissionId = slotBId,
            FocusSlot = SongWarsMatchSlot.B,
            Phase = SongWarsMatchPhase.Pending
        };

        tournament.Matches.Add(match);
        tournament.MatchOrder.Add(match.MatchId);
        return match;
    }

    private static void RequeueSkippedMatch(SongWarsTournament tournament, SongWarsMatch skipped)
    {
        var replay = new SongWarsMatch
        {
            MatchId = NewId("match"),
            Bracket = skipped.Bracket,
            RoundIndex = skipped.RoundIndex,
            RoundId = skipped.RoundId,
            SlotASubmissionId = skipped.SlotASubmissionId,
            SlotBSubmissionId = skipped.SlotBSubmissionId,
            FocusSlot = skipped.FocusSlot,
            Phase = SongWarsMatchPhase.Pending,
            ReplayedFromMatchId = skipped.MatchId
        };

        tournament.Matches.Add(replay);

        var insertAfter = tournament.MatchOrder
            .Select((id, index) => new { id, index })
            .Where(item =>
            {
                var match = tournament.Matches.FirstOrDefault(m => m.MatchId == item.id);
                return match?.RoundId == skipped.RoundId;
            })
            .Select(item => item.index)
            .DefaultIfEmpty(tournament.MatchOrder.Count - 1)
            .Max();

        tournament.MatchOrder.Insert(Math.Min(insertAfter + 1, tournament.MatchOrder.Count), replay.MatchId);
    }

    private static (string WinnerId, string LoserId, bool DirectElimination) ResolveResult(
        SongWarsMatch match,
        SongWarsOutcome outcome)
    {
        var focusId = match.FocusSlot == SongWarsMatchSlot.A ? match.SlotASubmissionId : match.SlotBSubmissionId;
        var otherId = match.FocusSlot == SongWarsMatchSlot.A ? match.SlotBSubmissionId : match.SlotASubmissionId;

        return outcome switch
        {
            SongWarsOutcome.Pass => (focusId, otherId, false),
            SongWarsOutcome.Fail => (otherId, focusId, false),
            SongWarsOutcome.Eliminated when match.Bracket == SongWarsBracket.Winners => (otherId, focusId, true),
            SongWarsOutcome.Eliminated => (otherId, focusId, false),
            _ => throw new InvalidOperationException($"Unsupported match outcome: {outcome}.")
        };
    }

    private static void ApplySubmissionResult(
        SongWarsTournament tournament,
        SongWarsMatch match,
        string winnerId,
        string loserId,
        bool directElimination)
    {
        var winner = FindSubmission(tournament, winnerId);
        var loser = FindSubmission(tournament, loserId);

        winner.Status = winner.Losses == 0
            ? SongWarsSubmissionStatus.Active
            : SongWarsSubmissionStatus.LosersBracket;

        if (directElimination)
        {
            loser.Status = SongWarsSubmissionStatus.Eliminated;
            loser.Losses = Math.Max(loser.Losses, 2);
            tournament.EliminationCount++;
            return;
        }

        loser.Losses++;
        loser.Status = loser.Losses >= 2
            ? SongWarsSubmissionStatus.Eliminated
            : SongWarsSubmissionStatus.LosersBracket;

        if (match.Bracket == SongWarsBracket.Losers && loser.Losses < 2)
        {
            loser.Losses = 2;
            loser.Status = SongWarsSubmissionStatus.Eliminated;
        }
    }

    private static SongWarsSubmission FindSubmission(SongWarsTournament tournament, string id) =>
        tournament.Submissions.First(s => string.Equals(s.SubmissionId, id, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<SongWarsSubmission> OrderedActiveSubmissions(SongWarsTournament tournament) =>
        tournament.Submissions
            .Where(s => s.Status is SongWarsSubmissionStatus.Active or SongWarsSubmissionStatus.LosersBracket)
            .OrderBy(s => s.Seed ?? int.MaxValue)
            .ThenBy(s => s.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.SubmissionId, StringComparer.OrdinalIgnoreCase);

    private static int NextRoundIndex(SongWarsTournament tournament, SongWarsBracket bracket) =>
        tournament.Matches
            .Where(m => m.Bracket == bracket)
            .Select(m => m.RoundIndex)
            .DefaultIfEmpty(0)
            .Max() + 1;

    private static string BuildRoundId(SongWarsBracket bracket, int roundIndex) =>
        bracket switch
        {
            SongWarsBracket.Winners => roundIndex switch
            {
                4 => "winners-semis",
                5 => "winners-finals",
                _ => $"winners-r{roundIndex}"
            },
            SongWarsBracket.Losers => roundIndex switch
            {
                4 => "losers-semis",
                5 => "losers-finals",
                _ => $"losers-r{roundIndex}"
            },
            SongWarsBracket.GrandFinals => roundIndex == 1 ? "grand-finals" : "grand-finals-reset",
            _ => $"{bracket.ToString().ToLowerInvariant()}-r{roundIndex}"
        };

    private static bool IsOpenMatch(SongWarsMatch match) =>
        match.Result == SongWarsOutcome.Pending &&
        match.Phase != SongWarsMatchPhase.Skipped;

    private static void SetCurrentToNextPending(SongWarsTournament tournament)
    {
        tournament.CurrentMatchId = tournament.MatchOrder
            .Select(id => tournament.Matches.FirstOrDefault(m => m.MatchId == id))
            .FirstOrDefault(m => m is not null && IsOpenMatch(m))
            ?.MatchId;
    }

    private static List<SongWarsSubmission> NormalizeSubmissions(
        IReadOnlyList<SongWarsSubmission> submissions,
        int? randomSeed)
    {
        var count = submissions.Count;
        var slots = new SongWarsSubmission?[count];
        var usedSeeds = new HashSet<int>();
        var unknown = new List<SongWarsSubmission>();

        foreach (var submission in submissions)
        {
            if (submission.Seed is { } seed && seed >= 1 && seed <= count)
            {
                if (!usedSeeds.Add(seed))
                    throw new ArgumentException($"Duplicate Song Wars seed {seed}.", nameof(submissions));

                slots[seed - 1] = CloneSubmission(submission, seed);
                continue;
            }

            unknown.Add(submission);
        }

        var random = randomSeed.HasValue ? new Random(randomSeed.Value) : Random.Shared;
        var shuffledUnknown = unknown.OrderBy(_ => random.Next()).ToList();
        var unknownIndex = 0;
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] is not null)
                continue;

            slots[i] = CloneSubmission(shuffledUnknown[unknownIndex], i + 1);
            unknownIndex++;
        }

        return slots.Select(s => s!).ToList();
    }

    private static SongWarsSubmission CloneSubmission(SongWarsSubmission source, int seed) =>
        new()
        {
            SubmissionId = string.IsNullOrWhiteSpace(source.SubmissionId) ? NewId("sub") : source.SubmissionId.Trim(),
            DisplayTitle = string.IsNullOrWhiteSpace(source.DisplayTitle) ? $"Seed {seed}" : source.DisplayTitle.Trim(),
            ArtistDisplayName = string.IsNullOrWhiteSpace(source.ArtistDisplayName) ? "Unknown Artist" : source.ArtistDisplayName.Trim(),
            Seed = seed,
            LocalFilePath = string.IsNullOrWhiteSpace(source.LocalFilePath) ? null : source.LocalFilePath.Trim(),
            ExternalSource = string.IsNullOrWhiteSpace(source.ExternalSource) ? null : source.ExternalSource.Trim(),
            DurationSeconds = source.DurationSeconds,
            Losses = 0,
            Status = SongWarsSubmissionStatus.Active
        };

    private static List<SongWarsJudge> NormalizeJudges(IReadOnlyList<SongWarsJudge> judges)
    {
        var normalized = new List<SongWarsJudge>();
        for (var i = 0; i < judges.Count; i++)
        {
            var judge = judges[i];
            normalized.Add(new SongWarsJudge
            {
                JudgeId = string.IsNullOrWhiteSpace(judge.JudgeId) ? NewId("judge") : judge.JudgeId.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(judge.DisplayName) ? $"Judge {i + 1}" : judge.DisplayName.Trim(),
                JoinToken = string.IsNullOrWhiteSpace(judge.JoinToken) ? Guid.NewGuid().ToString("N") : judge.JoinToken.Trim(),
                SharedPlayOptIn = judge.SharedPlayOptIn,
                LastSeenUtc = judge.LastSeenUtc
            });
        }

        return normalized;
    }

    private static void AppendAudit(SongWarsTournament tournament, string kind, string message, string? matchId = null) =>
        tournament.AuditLog.Add(new SongWarsAuditEntry
        {
            Kind = kind,
            Message = message,
            MatchId = matchId,
            AtUtc = DateTimeOffset.UtcNow
        });

    private static void Touch(SongWarsTournament tournament) =>
        tournament.UpdatedAtUtc = DateTimeOffset.UtcNow;

    private static string NewId(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
