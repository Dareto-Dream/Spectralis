namespace Spectralis.Core.SongWars;

public sealed class SongWarsSessionController
{
    private readonly Dictionary<string, List<SongWarsVote>> _votesByMatch = new(StringComparer.OrdinalIgnoreCase);

    public SongWarsSessionController(SongWarsTournament tournament)
    {
        Tournament = tournament ?? throw new ArgumentNullException(nameof(tournament));
    }

    public SongWarsTournament Tournament { get; }
    public SongWarsMatch? CurrentMatch => SongWarsBracketEngine.GetCurrentMatch(Tournament);
    public SongWarsTrackPointer? CurrentTrackA => CreateTrackPointer(CurrentMatch?.SlotASubmissionId);
    public SongWarsTrackPointer? CurrentTrackB => CreateTrackPointer(CurrentMatch?.SlotBSubmissionId);

    public void Start() => SongWarsBracketEngine.StartTournament(Tournament);

    public void BeginTrackA() => TransitionCurrent(SongWarsMatchPhase.TrackAPlaying, "track-a-started", "Track A started.",
        SongWarsMatchPhase.Pending, SongWarsMatchPhase.Ready, SongWarsMatchPhase.Paused);

    public void BeginTrackB() => TransitionCurrent(SongWarsMatchPhase.TrackBPlaying, "track-b-started", "Track B started.",
        SongWarsMatchPhase.TrackAPlaying, SongWarsMatchPhase.Paused);

    public void OpenPrimaryVoting() => TransitionCurrent(SongWarsMatchPhase.PrimaryVoting, "primary-voting-opened", "Primary voting opened.",
        SongWarsMatchPhase.TrackBPlaying, SongWarsMatchPhase.Paused);

    public void OpenEliminationVoting() => TransitionCurrent(SongWarsMatchPhase.EliminationVoting, "elimination-voting-opened", "Elimination voting opened.",
        SongWarsMatchPhase.PrimaryVoting, SongWarsMatchPhase.Paused);

    public void Pause()
    {
        var match = RequireCurrentMatch();
        if (match.Phase is SongWarsMatchPhase.Complete or SongWarsMatchPhase.Skipped or SongWarsMatchPhase.Reveal)
            throw new InvalidOperationException("Song Wars cannot pause a completed or revealed match.");
        if (match.Phase != SongWarsMatchPhase.Paused)
            match.PausedFromPhase = match.Phase;
        match.Phase = SongWarsMatchPhase.Paused;
        AppendAudit("paused", "Match paused.", match.MatchId);
        Touch();
    }

    public void Resume()
    {
        var match = RequireCurrentMatch();
        if (match.Phase != SongWarsMatchPhase.Paused)
            throw new InvalidOperationException("Song Wars can only resume a paused match.");
        match.Phase = match.PausedFromPhase ?? SongWarsMatchPhase.Ready;
        match.PausedFromPhase = null;
        AppendAudit("resumed", "Match resumed.", match.MatchId);
        Touch();
    }

    public void SubmitVote(string judgeId, SongWarsVoteChoice choice, SongWarsVotePhase phase = SongWarsVotePhase.Primary)
    {
        var match = RequireCurrentMatch();
        var judge = ValidateVoteSubmission(match, judgeId, choice, phase);
        var (slotAChoice, slotBChoice) = choice == SongWarsVoteChoice.Eliminated
            ? ((SongWarsTrackVoteChoice?)null, (SongWarsTrackVoteChoice?)null)
            : CreateComplementaryTrackChoices(match.FocusSlot, choice == SongWarsVoteChoice.Pass ? SongWarsTrackVoteChoice.Pass : SongWarsTrackVoteChoice.Fail);
        AppendVote(judge, match, phase, choice, slotAChoice, slotBChoice);
    }

    public void SubmitTrackVote(string judgeId, SongWarsMatchSlot slot, SongWarsTrackVoteChoice choice, SongWarsVotePhase phase = SongWarsVotePhase.Primary)
    {
        var match = RequireCurrentMatch();
        var effectiveChoice = CreateEffectiveChoice(match.FocusSlot, slot, choice);
        var judge = ValidateVoteSubmission(match, judgeId, effectiveChoice, phase);
        var (slotAChoice, slotBChoice) = CreateComplementaryTrackChoices(slot, choice);
        AppendVote(judge, match, phase, effectiveChoice, slotAChoice, slotBChoice);
    }

    public void SubmitEliminationVote(string judgeId, SongWarsMatchSlot eliminatedSlot, SongWarsVotePhase phase = SongWarsVotePhase.Primary)
    {
        var match = RequireCurrentMatch();
        var judge = ValidateVoteSubmission(match, judgeId, SongWarsVoteChoice.Eliminated, phase);
        var survivingSlot = eliminatedSlot == SongWarsMatchSlot.A ? SongWarsMatchSlot.B : SongWarsMatchSlot.A;
        var (slotAChoice, slotBChoice) = CreateComplementaryTrackChoices(survivingSlot, SongWarsTrackVoteChoice.Pass);
        AppendVote(judge, match, phase, SongWarsVoteChoice.Eliminated, slotAChoice, slotBChoice);
    }

    public SongWarsVoteTallyResult RevealCurrent(bool timerExpired)
    {
        var match = RequireCurrentMatch();
        if (match.Phase is not SongWarsMatchPhase.PrimaryVoting and not SongWarsMatchPhase.EliminationVoting)
            throw new InvalidOperationException("Song Wars can only reveal from a voting phase.");

        var phase = match.Phase == SongWarsMatchPhase.EliminationVoting ? SongWarsVotePhase.Elimination : SongWarsVotePhase.Primary;

        var tally = SongWarsVoteTally.Tally(new SongWarsVoteTallyInput(
            match.Bracket, Tournament.Judges.Count,
            GetMatchVotes(match.MatchId), phase,
            Tournament.EliminationCount, Tournament.LastMatchDirectEliminated,
            timerExpired, match.FocusSlot));

        if (tally.Outcome == SongWarsOutcome.Pending) return tally;

        match.Phase = SongWarsMatchPhase.Reveal;
        var snapshot = new SongWarsVoteSnapshot
        {
            Phase = phase, Outcome = tally.Outcome,
            PassCount = tally.PassCount, FailCount = tally.FailCount, EliminatedCount = tally.EliminatedCount,
            TrackAPassCount = tally.TrackAPassCount, TrackAFailCount = tally.TrackAFailCount,
            TrackBPassCount = tally.TrackBPassCount, TrackBFailCount = tally.TrackBFailCount,
            EliminatedSlot = tally.EliminatedSlot,
            RuleApplied = tally.RuleApplied, Explanation = tally.Explanation,
            RevealedAtUtc = DateTimeOffset.UtcNow
        };

        if (tally.Outcome == SongWarsOutcome.Eliminated && tally.EliminatedSlot is { } eliminatedSlot)
            match.FocusSlot = eliminatedSlot;

        SongWarsBracketEngine.AdvanceMatch(Tournament, match.MatchId, tally.Outcome, snapshot);
        AppendAudit("revealed", tally.Explanation, match.MatchId);
        Touch();
        return tally;
    }

    public SongWarsVoteTallyResult TallyCurrentLive()
    {
        var match = RequireCurrentMatch();
        var phase = match.Phase == SongWarsMatchPhase.EliminationVoting ? SongWarsVotePhase.Elimination : SongWarsVotePhase.Primary;
        return SongWarsVoteTally.Tally(new SongWarsVoteTallyInput(
            match.Bracket, Tournament.Judges.Count,
            GetMatchVotes(match.MatchId), phase,
            Tournament.EliminationCount, Tournament.LastMatchDirectEliminated,
            TimerExpired: false, match.FocusSlot));
    }

    public IReadOnlyList<SongWarsVote> GetVotes(string matchId) =>
        _votesByMatch.TryGetValue(matchId, out var votes) ? votes.ToList() : [];

    public void LoadVotes(string matchId, IEnumerable<SongWarsVote> votes) =>
        _votesByMatch[matchId] = votes
            .Where(v => string.Equals(v.MatchId, matchId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.SubmittedAtUtc).ThenBy(v => v.Revision).ToList();

    private SongWarsMatch RequireCurrentMatch() =>
        CurrentMatch ?? throw new InvalidOperationException("Song Wars does not have a current match.");

    private void TransitionCurrent(SongWarsMatchPhase nextPhase, string auditKind, string auditMessage, params SongWarsMatchPhase[] allowedPhases)
    {
        var match = RequireCurrentMatch();
        if (!allowedPhases.Contains(match.Phase))
            throw new InvalidOperationException($"Song Wars cannot transition from {match.Phase} to {nextPhase}.");
        if (match.StartedAtUtc is null) match.StartedAtUtc = DateTimeOffset.UtcNow;
        match.Phase = nextPhase;
        AppendAudit(auditKind, auditMessage, match.MatchId);
        Touch();
    }

    private List<SongWarsVote> GetMatchVotes(string matchId)
    {
        if (!_votesByMatch.TryGetValue(matchId, out var votes)) { votes = []; _votesByMatch[matchId] = votes; }
        return votes;
    }

    private SongWarsJudge ValidateVoteSubmission(SongWarsMatch match, string judgeId, SongWarsVoteChoice choice, SongWarsVotePhase phase)
    {
        var expectedPhase = phase == SongWarsVotePhase.Primary ? SongWarsMatchPhase.PrimaryVoting : SongWarsMatchPhase.EliminationVoting;
        if (match.Phase != expectedPhase)
            throw new InvalidOperationException("Song Wars votes can only be submitted during the active voting window.");
        var judge = Tournament.Judges.FirstOrDefault(j => string.Equals(j.JudgeId, judgeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Song Wars could not find the requested judge.");
        if (match.Bracket != SongWarsBracket.Winners && choice == SongWarsVoteChoice.Eliminated)
            throw new InvalidOperationException("Song Wars only allows Eliminated votes in the winners bracket.");
        return judge;
    }

    private static (SongWarsTrackVoteChoice SlotA, SongWarsTrackVoteChoice SlotB) CreateComplementaryTrackChoices(SongWarsMatchSlot slot, SongWarsTrackVoteChoice choice)
    {
        var opposite = Opposite(choice);
        return slot == SongWarsMatchSlot.A ? (choice, opposite) : (opposite, choice);
    }

    private static SongWarsVoteChoice CreateEffectiveChoice(SongWarsMatchSlot focusSlot, SongWarsMatchSlot selectedSlot, SongWarsTrackVoteChoice selectedChoice)
    {
        var focusChoice = selectedSlot == focusSlot ? selectedChoice : Opposite(selectedChoice);
        return focusChoice == SongWarsTrackVoteChoice.Pass ? SongWarsVoteChoice.Pass : SongWarsVoteChoice.Fail;
    }

    private static SongWarsTrackVoteChoice Opposite(SongWarsTrackVoteChoice choice) =>
        choice == SongWarsTrackVoteChoice.Pass ? SongWarsTrackVoteChoice.Fail : SongWarsTrackVoteChoice.Pass;

    private SongWarsTrackPointer? CreateTrackPointer(string? submissionId)
    {
        if (string.IsNullOrWhiteSpace(submissionId)) return null;
        var sub = Tournament.Submissions.FirstOrDefault(s => string.Equals(s.SubmissionId, submissionId, StringComparison.OrdinalIgnoreCase));
        return sub is null ? null : new SongWarsTrackPointer(sub.SubmissionId, sub.DisplayTitle, sub.ArtistDisplayName, sub.LocalFilePath, sub.ExternalSource, sub.DurationSeconds);
    }

    private void AppendVote(SongWarsJudge judge, SongWarsMatch match, SongWarsVotePhase phase,
        SongWarsVoteChoice choice, SongWarsTrackVoteChoice? slotA, SongWarsTrackVoteChoice? slotB)
    {
        var votes = GetMatchVotes(match.MatchId);
        var prevRevision = votes.Where(v => string.Equals(v.JudgeId, judge.JudgeId, StringComparison.OrdinalIgnoreCase) && v.Phase == phase)
            .Select(v => v.Revision).DefaultIfEmpty(0).Max();
        votes.Add(new SongWarsVote
        {
            JudgeId = judge.JudgeId,
            MatchId = match.MatchId,
            Phase = phase,
            Choice = choice,
            SlotAChoice = slotA,
            SlotBChoice = slotB,
            SubmittedAtUtc = DateTimeOffset.UtcNow,
            Revision = prevRevision + 1
        });
        AppendAudit("vote", $"{judge.DisplayName} voted {choice}.", match.MatchId);
        Touch();
    }

    private void AppendAudit(string kind, string message, string? matchId = null) =>
        Tournament.AuditLog.Add(new SongWarsAuditEntry { Kind = kind, Message = message, MatchId = matchId, AtUtc = DateTimeOffset.UtcNow });

    private void Touch() => Tournament.UpdatedAtUtc = DateTimeOffset.UtcNow;
}
