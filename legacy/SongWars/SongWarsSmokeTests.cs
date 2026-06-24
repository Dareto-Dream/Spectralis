using System.IO;

namespace Spectralis;

/// <summary>
/// In-process smoke tests for the local Song Wars tournament core.
/// Call SongWarsSmokeTests.RunAll() from a debug hook while developing host/backend surfaces.
/// </summary>
internal static class SongWarsSmokeTests
{
    private sealed record TestResult(string Name, bool Passed, string? Error = null);

    public static void RunAll()
    {
        var results = new List<TestResult>
        {
            Run("Tally_OneJudge_Direct", Test_Tally_OneJudge_Direct),
            Run("Tally_TwoJudge_TieSkips", Test_Tally_TwoJudge_TieSkips),
            Run("Tally_ThreeJudge_Majority", Test_Tally_ThreeJudge_Majority),
            Run("Tally_FourJudge_TieSkips", Test_Tally_FourJudge_TieSkips),
            Run("Tally_FiveJudge_Majority", Test_Tally_FiveJudge_Majority),
            Run("Tally_Winners_EliminatedMajority", Test_Tally_Winners_EliminatedMajority),
            Run("Tally_Winners_EliminatedConvertsToFail", Test_Tally_Winners_EliminatedConvertsToFail),
            Run("Tally_Losers_EliminatedConvertsToFail", Test_Tally_Losers_EliminatedConvertsToFail),
            Run("Tally_EliminationCap_BlocksEliminated", Test_Tally_EliminationCap_BlocksEliminated),
            Run("Tally_BackToBack_BlocksEliminated", Test_Tally_BackToBack_BlocksEliminated),
            Run("Tally_TimerExpiry_MissingVotesSkips", Test_Tally_TimerExpiry_MissingVotesSkips),
            Run("Tally_AllJudgesSubmitted_Reveals", Test_Tally_AllJudgesSubmitted_Reveals),
            Run("Tally_TrackPair_ComplementsChoices", Test_Tally_TrackPair_ComplementsChoices),
            Run("Bracket_InitialGeneration", Test_Bracket_InitialGeneration),
            Run("Bracket_LosersRoundAfterWinnersRound", Test_Bracket_LosersRoundAfterWinnersRound),
            Run("Bracket_SkipRequeuesMatch", Test_Bracket_SkipRequeuesMatch),
            Run("Controller_SubmitTrackVote_ComplementsOppositeTrack", Test_Controller_SubmitTrackVote_ComplementsOppositeTrack),
            Run("Store_RoundTrip", Test_Store_RoundTrip)
        };

        var failed = results.Where(r => !r.Passed).ToList();
        var message = $"Song Wars Smoke Tests: {results.Count - failed.Count}/{results.Count} passed";
        System.Diagnostics.Debug.WriteLine(message);
        foreach (var failure in failed)
            System.Diagnostics.Debug.WriteLine($"  FAIL [{failure.Name}]: {failure.Error}");

        if (failed.Count > 0)
            throw new Exception($"{message}{Environment.NewLine}{string.Join(Environment.NewLine, failed.Select(f => $"{f.Name}: {f.Error}"))}");
    }

    private static TestResult Run(string name, Action test)
    {
        try
        {
            test();
            return new TestResult(name, true);
        }
        catch (Exception ex)
        {
            return new TestResult(name, false, ex.Message);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private static void Test_Tally_OneJudge_Direct()
    {
        var result = Tally(1, [SongWarsVoteChoice.Pass]);
        Assert(result.Outcome == SongWarsOutcome.Pass, "One judge Pass should pass.");
    }

    private static void Test_Tally_TwoJudge_TieSkips()
    {
        var result = Tally(2, [SongWarsVoteChoice.Pass, SongWarsVoteChoice.Fail]);
        Assert(result.Outcome == SongWarsOutcome.Skip, "Two judge 1-1 tie should skip.");
    }

    private static void Test_Tally_ThreeJudge_Majority()
    {
        var result = Tally(3, [SongWarsVoteChoice.Fail, SongWarsVoteChoice.Pass, SongWarsVoteChoice.Fail]);
        Assert(result.Outcome == SongWarsOutcome.Fail, "Three judge 2-vote majority should fail.");
    }

    private static void Test_Tally_FourJudge_TieSkips()
    {
        var result = Tally(4, [SongWarsVoteChoice.Pass, SongWarsVoteChoice.Pass, SongWarsVoteChoice.Fail, SongWarsVoteChoice.Fail]);
        Assert(result.Outcome == SongWarsOutcome.Skip, "Four judge 2-2 tie should skip.");
    }

    private static void Test_Tally_FiveJudge_Majority()
    {
        var result = Tally(5, [SongWarsVoteChoice.Pass, SongWarsVoteChoice.Pass, SongWarsVoteChoice.Pass]);
        Assert(result.Outcome == SongWarsOutcome.Pass, "Five judge tally needs 3 votes.");
    }

    private static void Test_Tally_Winners_EliminatedMajority()
    {
        var result = Tally(3, [SongWarsVoteChoice.Eliminated, SongWarsVoteChoice.Eliminated, SongWarsVoteChoice.Fail]);
        Assert(result.Outcome == SongWarsOutcome.Eliminated, "Eliminated majority should eliminate in winners bracket.");
    }

    private static void Test_Tally_Winners_EliminatedConvertsToFail()
    {
        var result = Tally(3, [SongWarsVoteChoice.Eliminated, SongWarsVoteChoice.Fail, SongWarsVoteChoice.Pass]);
        Assert(result.Outcome == SongWarsOutcome.Fail, "Non-majority Eliminated should convert to Fail.");
        Assert(result.ConvertedEliminatedVotes, "Conversion should be reported.");
    }

    private static void Test_Tally_Losers_EliminatedConvertsToFail()
    {
        var result = Tally(
            3,
            [SongWarsVoteChoice.Eliminated, SongWarsVoteChoice.Pass, SongWarsVoteChoice.Fail],
            bracket: SongWarsBracket.Losers);
        Assert(result.Outcome == SongWarsOutcome.Fail, "Losers bracket Eliminated should convert to Fail.");
    }

    private static void Test_Tally_EliminationCap_BlocksEliminated()
    {
        var result = Tally(
            3,
            [SongWarsVoteChoice.Eliminated, SongWarsVoteChoice.Eliminated, SongWarsVoteChoice.Pass],
            eliminationsUsed: SongWarsVoteTally.MaxDirectEliminations);
        Assert(result.Outcome == SongWarsOutcome.Fail, "Elimination cap should convert Eliminated to Fail.");
    }

    private static void Test_Tally_BackToBack_BlocksEliminated()
    {
        var result = Tally(
            3,
            [SongWarsVoteChoice.Eliminated, SongWarsVoteChoice.Eliminated, SongWarsVoteChoice.Pass],
            previousDirectElimination: true);
        Assert(result.Outcome == SongWarsOutcome.Fail, "Back-to-back direct elimination should be blocked.");
    }

    private static void Test_Tally_TimerExpiry_MissingVotesSkips()
    {
        var result = Tally(5, [SongWarsVoteChoice.Pass, SongWarsVoteChoice.Pass], timerExpired: true);
        Assert(result.Outcome == SongWarsOutcome.Skip, "Timer expiry without threshold should skip.");
    }

    private static void Test_Tally_AllJudgesSubmitted_Reveals()
    {
        var result = Tally(
            5,
            [
                SongWarsVoteChoice.Pass,
                SongWarsVoteChoice.Fail,
                SongWarsVoteChoice.Pass,
                SongWarsVoteChoice.Pass,
                SongWarsVoteChoice.Fail
            ],
            timerExpired: false);
        Assert(result.Outcome == SongWarsOutcome.Pass, "All judges submitted should reveal without timer expiry.");
        Assert(result.AllJudgesSubmitted, "All judges flag should be set.");
    }

    private static void Test_Tally_TrackPair_ComplementsChoices()
    {
        var votes = new List<SongWarsVote>
        {
            CreateTrackVote("judge-1", SongWarsTrackVoteChoice.Pass, SongWarsTrackVoteChoice.Fail),
            CreateTrackVote("judge-2", SongWarsTrackVoteChoice.Pass, SongWarsTrackVoteChoice.Fail),
            CreateTrackVote("judge-3", SongWarsTrackVoteChoice.Fail, SongWarsTrackVoteChoice.Pass)
        };

        var result = SongWarsVoteTally.Tally(new SongWarsVoteTallyInput(
            SongWarsBracket.Winners,
            3,
            votes,
            SongWarsVotePhase.Primary,
            0,
            false,
            true,
            SongWarsMatchSlot.B));

        Assert(result.Outcome == SongWarsOutcome.Fail, "Track A Pass / Track B Fail should count as focused Track B Fail.");
        Assert(result.TrackAPassCount == 2, "Track A should have two Pass votes.");
        Assert(result.TrackBFailCount == 2, "Track B should have two Fail votes.");
    }

    private static void Test_Bracket_InitialGeneration()
    {
        var tournament = CreateTournament();
        Assert(tournament.Matches.Count == 16, "Initial winners round should have 16 matches.");
        Assert(tournament.Matches.All(m => m.Bracket == SongWarsBracket.Winners), "Initial matches should be winners bracket.");
        Assert(!string.IsNullOrWhiteSpace(tournament.CurrentMatchId), "Current match should be set.");
    }

    private static void Test_Bracket_LosersRoundAfterWinnersRound()
    {
        var tournament = CreateTournament();
        var initialMatches = tournament.Matches.ToList();
        foreach (var match in initialMatches)
            SongWarsBracketEngine.AdvanceMatch(tournament, match.MatchId, SongWarsOutcome.Pass);

        Assert(tournament.Matches.Count(m => m.Bracket == SongWarsBracket.Losers && m.RoundIndex == 1) == 8,
            "Completing winners round 1 should create 8 losers round matches.");
    }

    private static void Test_Bracket_SkipRequeuesMatch()
    {
        var tournament = CreateTournament();
        var current = SongWarsBracketEngine.GetCurrentMatch(tournament)!;
        SongWarsBracketEngine.AdvanceMatch(tournament, current.MatchId, SongWarsOutcome.Skip);
        Assert(tournament.Matches.Any(m => m.ReplayedFromMatchId == current.MatchId), "Skipped match should create replay match.");
    }

    private static void Test_Controller_SubmitTrackVote_ComplementsOppositeTrack()
    {
        var tournament = CreateTournament();
        var controller = new SongWarsSessionController(tournament);
        controller.Start();
        controller.BeginTrackA();
        controller.BeginTrackB();
        controller.OpenPrimaryVoting();

        controller.SubmitTrackVote("judge-1", SongWarsMatchSlot.A, SongWarsTrackVoteChoice.Pass);
        var vote = controller.GetVotes(controller.CurrentMatch!.MatchId).Single();

        Assert(vote.SlotAChoice == SongWarsTrackVoteChoice.Pass, "Track A should store Pass.");
        Assert(vote.SlotBChoice == SongWarsTrackVoteChoice.Fail, "Track B should auto-store Fail.");
        Assert(vote.Choice == SongWarsVoteChoice.Fail, "Focused Track B should resolve to Fail when Track A passes.");
    }

    private static void Test_Store_RoundTrip()
    {
        var root = Path.Combine(Path.GetTempPath(), $"spectralis-songwars-{Guid.NewGuid():N}");
        try
        {
            var store = new SongWarsTournamentStore(root);
            var tournament = CreateTournament();
            store.SaveTournamentAsync(tournament).GetAwaiter().GetResult();
            var loaded = store.LoadTournamentAsync(tournament.TournamentId).GetAwaiter().GetResult();
            Assert(loaded is not null, "Tournament should load after save.");
            Assert(loaded!.Matches.Count == tournament.Matches.Count, "Loaded match count should match.");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static SongWarsVoteTallyResult Tally(
        int judgeCount,
        IReadOnlyList<SongWarsVoteChoice> choices,
        SongWarsBracket bracket = SongWarsBracket.Winners,
        int eliminationsUsed = 0,
        bool previousDirectElimination = false,
        bool timerExpired = true)
    {
        var votes = choices
            .Select((choice, index) => new SongWarsVote
            {
                JudgeId = $"judge-{index + 1}",
                MatchId = "match-1",
                Phase = SongWarsVotePhase.Primary,
                Choice = choice,
                Revision = 1,
                SubmittedAtUtc = DateTimeOffset.UtcNow.AddSeconds(index)
            })
            .ToList();

        return SongWarsVoteTally.Tally(new SongWarsVoteTallyInput(
            bracket,
            judgeCount,
            votes,
            SongWarsVotePhase.Primary,
            eliminationsUsed,
            previousDirectElimination,
            timerExpired));
    }

    private static SongWarsVote CreateTrackVote(
        string judgeId,
        SongWarsTrackVoteChoice slotAChoice,
        SongWarsTrackVoteChoice slotBChoice) =>
        new()
        {
            JudgeId = judgeId,
            MatchId = "match-1",
            Phase = SongWarsVotePhase.Primary,
            Choice = slotBChoice == SongWarsTrackVoteChoice.Pass
                ? SongWarsVoteChoice.Pass
                : SongWarsVoteChoice.Fail,
            SlotAChoice = slotAChoice,
            SlotBChoice = slotBChoice,
            Revision = 1,
            SubmittedAtUtc = DateTimeOffset.UtcNow
        };

    private static SongWarsTournament CreateTournament()
    {
        var submissions = Enumerable.Range(1, 32)
            .Select(i => new SongWarsSubmission
            {
                SubmissionId = $"sub-{i}",
                DisplayTitle = $"Song {i}",
                ArtistDisplayName = $"Artist {i}",
                Seed = i,
                LocalFilePath = $"C:\\Music\\song-{i}.mp3"
            })
            .ToList();

        var judges = Enumerable.Range(1, 3)
            .Select(i => new SongWarsJudge
            {
                JudgeId = $"judge-{i}",
                DisplayName = $"Judge {i}",
                JoinToken = $"token-{i}"
            })
            .ToList();

        return SongWarsBracketEngine.CreateTournament("Smoke Test", submissions, judges, randomSeed: 123);
    }
}
