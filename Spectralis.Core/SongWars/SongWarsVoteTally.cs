namespace Spectralis.Core.SongWars;

public sealed record SongWarsVoteTallyInput(
    SongWarsBracket Bracket,
    int JudgeCount,
    IReadOnlyCollection<SongWarsVote> Votes,
    SongWarsVotePhase Phase,
    int EliminationsUsed,
    bool PreviousMatchDirectEliminated,
    bool TimerExpired,
    SongWarsMatchSlot FocusSlot = SongWarsMatchSlot.B);

public sealed class SongWarsVoteTallyResult
{
    public SongWarsOutcome Outcome { get; init; } = SongWarsOutcome.Pending;
    public int PassCount { get; init; }
    public int FailCount { get; init; }
    public int EliminatedCount { get; init; }
    public int TrackAPassCount { get; init; }
    public int TrackAFailCount { get; init; }
    public int TrackBPassCount { get; init; }
    public int TrackBFailCount { get; init; }
    public SongWarsMatchSlot? EliminatedSlot { get; init; }
    public int SubmittedJudgeCount { get; init; }
    public bool AllJudgesSubmitted { get; init; }
    public bool RequiresEliminationVote { get; init; }
    public bool ConvertedEliminatedVotes { get; init; }
    public string RuleApplied { get; init; } = "";
    public string Explanation { get; init; } = "";
}

public static class SongWarsVoteTally
{
    public const int MaxDirectEliminations = 9;

    public static SongWarsVoteTallyResult Tally(SongWarsVoteTallyInput input)
    {
        if (input.JudgeCount is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(input), "Song Wars supports 1-5 judges.");

        var latestVotes = input.Votes
            .Where(v => v.Phase == input.Phase)
            .GroupBy(v => v.JudgeId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(v => v.Revision).ThenByDescending(v => v.SubmittedAtUtc).First())
            .Take(input.JudgeCount)
            .ToList();

        var resolvedVotes = latestVotes.Select(v => ResolveVoteChoice(v, input.FocusSlot)).ToList();

        var submitted = resolvedVotes.Count;
        var allSubmitted = submitted >= input.JudgeCount;
        if (!allSubmitted && !input.TimerExpired)
        {
            return new SongWarsVoteTallyResult
            {
                Outcome = SongWarsOutcome.Pending,
                SubmittedJudgeCount = submitted,
                AllJudgesSubmitted = false,
                PassCount = resolvedVotes.Count(v => v.EffectiveChoice == SongWarsVoteChoice.Pass),
                FailCount = resolvedVotes.Count(v => v.EffectiveChoice == SongWarsVoteChoice.Fail),
                EliminatedCount = resolvedVotes.Count(v => v.EffectiveChoice == SongWarsVoteChoice.Eliminated),
                TrackAPassCount = resolvedVotes.Count(v => v.SlotAChoice == SongWarsTrackVoteChoice.Pass),
                TrackAFailCount = resolvedVotes.Count(v => v.SlotAChoice == SongWarsTrackVoteChoice.Fail),
                TrackBPassCount = resolvedVotes.Count(v => v.SlotBChoice == SongWarsTrackVoteChoice.Pass),
                TrackBFailCount = resolvedVotes.Count(v => v.SlotBChoice == SongWarsTrackVoteChoice.Fail),
                RuleApplied = "waiting-for-votes",
                Explanation = "Voting is still open."
            };
        }

        var threshold = RequiredVotes(input.JudgeCount);
        var pass = resolvedVotes.Count(v => v.EffectiveChoice == SongWarsVoteChoice.Pass);
        var fail = resolvedVotes.Count(v => v.EffectiveChoice == SongWarsVoteChoice.Fail);
        var eliminated = resolvedVotes.Count(v => v.EffectiveChoice == SongWarsVoteChoice.Eliminated);
        var eliminatedA = resolvedVotes.Count(v => EliminatedSlot(v, input.FocusSlot) == SongWarsMatchSlot.A);
        var eliminatedB = resolvedVotes.Count(v => EliminatedSlot(v, input.FocusSlot) == SongWarsMatchSlot.B);
        var trackAPass = resolvedVotes.Count(v => v.SlotAChoice == SongWarsTrackVoteChoice.Pass);
        var trackAFail = resolvedVotes.Count(v => v.SlotAChoice == SongWarsTrackVoteChoice.Fail);
        var trackBPass = resolvedVotes.Count(v => v.SlotBChoice == SongWarsTrackVoteChoice.Pass);
        var trackBFail = resolvedVotes.Count(v => v.SlotBChoice == SongWarsTrackVoteChoice.Fail);

        var converted = false;
        var conversionReason = "";

        if (input.Bracket != SongWarsBracket.Winners)
        {
            AddConvertedEliminations(input.FocusSlot, eliminatedA, eliminatedB, ref pass, ref fail);
            converted = eliminated > 0;
            conversionReason = converted ? "Losers bracket does not allow Eliminated votes." : "";
            eliminated = 0;
        }
        else if (input.EliminationsUsed >= MaxDirectEliminations)
        {
            AddConvertedEliminations(input.FocusSlot, eliminatedA, eliminatedB, ref pass, ref fail);
            converted = eliminated > 0;
            conversionReason = converted ? "Direct elimination cap reached." : "";
            eliminated = 0;
        }
        else if (input.PreviousMatchDirectEliminated)
        {
            AddConvertedEliminations(input.FocusSlot, eliminatedA, eliminatedB, ref pass, ref fail);
            converted = eliminated > 0;
            conversionReason = converted ? "Direct eliminations cannot happen in consecutive matches." : "";
            eliminated = 0;
        }

        if (input.Bracket == SongWarsBracket.Winners && eliminatedA >= threshold && eliminatedA > eliminatedB)
            return Create(SongWarsOutcome.Eliminated, pass, fail, eliminated, trackAPass, trackAFail, trackBPass, trackBFail, submitted, allSubmitted, converted, SongWarsMatchSlot.A, "eliminated-majority", "Track A elimination reached the judge threshold.");

        if (input.Bracket == SongWarsBracket.Winners && eliminatedB >= threshold && eliminatedB > eliminatedA)
            return Create(SongWarsOutcome.Eliminated, pass, fail, eliminated, trackAPass, trackAFail, trackBPass, trackBFail, submitted, allSubmitted, converted, SongWarsMatchSlot.B, "eliminated-majority", "Track B elimination reached the judge threshold.");

        if (input.Bracket == SongWarsBracket.Winners && eliminated > 0)
        {
            AddConvertedEliminations(input.FocusSlot, eliminatedA, eliminatedB, ref pass, ref fail);
            converted = true;
            conversionReason = "Eliminated did not reach majority and was converted to Fail.";
            eliminated = 0;
        }

        if (pass == fail && pass >= threshold)
            return Create(SongWarsOutcome.Skip, pass, fail, eliminated, trackAPass, trackAFail, trackBPass, trackBFail, submitted, allSubmitted, converted, null, converted ? "converted-tie-skip" : "tie-skip",
                string.IsNullOrWhiteSpace(conversionReason) ? "The vote tied; match skipped and replayed." : $"{conversionReason} Converted vote tied; match skipped.");

        if (pass >= threshold && pass > fail)
            return Create(SongWarsOutcome.Pass, pass, fail, eliminated, trackAPass, trackAFail, trackBPass, trackBFail, submitted, allSubmitted, converted, null, converted ? "converted-majority-pass" : "majority-pass",
                string.IsNullOrWhiteSpace(conversionReason) ? "Pass reached the judge threshold." : $"{conversionReason} Pass reached the judge threshold.");

        if (fail >= threshold && fail > pass)
            return Create(SongWarsOutcome.Fail, pass, fail, eliminated, trackAPass, trackAFail, trackBPass, trackBFail, submitted, allSubmitted, converted, null, converted ? "converted-majority-fail" : "majority-fail",
                string.IsNullOrWhiteSpace(conversionReason) ? "Fail reached the judge threshold." : $"{conversionReason} Fail reached the judge threshold.");

        return Create(SongWarsOutcome.Skip, pass, fail, eliminated, trackAPass, trackAFail, trackBPass, trackBFail, submitted, allSubmitted, converted, null, converted ? "converted-no-majority-skip" : "no-majority-skip",
            string.IsNullOrWhiteSpace(conversionReason) ? "No choice reached the judge threshold before reveal." : $"{conversionReason} No majority before reveal.");
    }

    private static int RequiredVotes(int judgeCount) =>
        judgeCount switch { 1 => 1, 2 => 2, 3 => 2, 4 => 2, 5 => 3, _ => throw new ArgumentOutOfRangeException(nameof(judgeCount)) };

    private static SongWarsVoteTallyResult Create(
        SongWarsOutcome outcome, int pass, int fail, int eliminated,
        int trackAPass, int trackAFail, int trackBPass, int trackBFail,
        int submitted, bool allSubmitted, bool converted,
        SongWarsMatchSlot? eliminatedSlot, string rule, string explanation) =>
        new()
        {
            Outcome = outcome, PassCount = pass, FailCount = fail, EliminatedCount = eliminated,
            TrackAPassCount = trackAPass, TrackAFailCount = trackAFail,
            TrackBPassCount = trackBPass, TrackBFailCount = trackBFail,
            SubmittedJudgeCount = submitted, AllJudgesSubmitted = allSubmitted,
            ConvertedEliminatedVotes = converted, EliminatedSlot = eliminatedSlot,
            RuleApplied = rule, Explanation = explanation
        };

    private static void AddConvertedEliminations(SongWarsMatchSlot focusSlot, int eliminatedA, int eliminatedB, ref int pass, ref int fail)
    {
        AddForSlot(focusSlot, SongWarsMatchSlot.A, eliminatedA, ref pass, ref fail);
        AddForSlot(focusSlot, SongWarsMatchSlot.B, eliminatedB, ref pass, ref fail);
    }

    private static void AddForSlot(SongWarsMatchSlot focusSlot, SongWarsMatchSlot slot, int count, ref int pass, ref int fail)
    {
        if (count <= 0) return;
        if (slot == focusSlot) fail += count; else pass += count;
    }

    private static SongWarsMatchSlot? EliminatedSlot(ResolvedVote vote, SongWarsMatchSlot focusSlot)
    {
        if (vote.EffectiveChoice != SongWarsVoteChoice.Eliminated) return null;
        if (vote.SlotAChoice == SongWarsTrackVoteChoice.Fail && vote.SlotBChoice == SongWarsTrackVoteChoice.Pass) return SongWarsMatchSlot.A;
        if (vote.SlotBChoice == SongWarsTrackVoteChoice.Fail && vote.SlotAChoice == SongWarsTrackVoteChoice.Pass) return SongWarsMatchSlot.B;
        return focusSlot;
    }

    private static ResolvedVote ResolveVoteChoice(SongWarsVote vote, SongWarsMatchSlot focusSlot)
    {
        if (vote.Choice == SongWarsVoteChoice.Eliminated)
            return new ResolvedVote(SongWarsVoteChoice.Eliminated, vote.SlotAChoice, vote.SlotBChoice);

        var slotA = vote.SlotAChoice;
        var slotB = vote.SlotBChoice;

        if (slotA.HasValue || slotB.HasValue)
        {
            if (!slotA.HasValue || !slotB.HasValue)
                throw new InvalidOperationException("Song Wars track votes must include both Track A and Track B choices.");
            if (slotA.Value == slotB.Value)
                throw new InvalidOperationException("Song Wars track votes must be complementary.");
            var focusedChoice = focusSlot == SongWarsMatchSlot.A ? slotA.Value : slotB.Value;
            return new ResolvedVote(focusedChoice == SongWarsTrackVoteChoice.Pass ? SongWarsVoteChoice.Pass : SongWarsVoteChoice.Fail, slotA, slotB);
        }

        var legacySlotA = InferSlotChoice(vote.Choice, focusSlot, SongWarsMatchSlot.A);
        var legacySlotB = InferSlotChoice(vote.Choice, focusSlot, SongWarsMatchSlot.B);
        return new ResolvedVote(vote.Choice, legacySlotA, legacySlotB);
    }

    private static SongWarsTrackVoteChoice InferSlotChoice(SongWarsVoteChoice choice, SongWarsMatchSlot focusSlot, SongWarsMatchSlot slot)
    {
        var focusPasses = choice == SongWarsVoteChoice.Pass;
        var slotPasses = slot == focusSlot ? focusPasses : !focusPasses;
        return slotPasses ? SongWarsTrackVoteChoice.Pass : SongWarsTrackVoteChoice.Fail;
    }

    private sealed record ResolvedVote(SongWarsVoteChoice EffectiveChoice, SongWarsTrackVoteChoice? SlotAChoice, SongWarsTrackVoteChoice? SlotBChoice);
}
