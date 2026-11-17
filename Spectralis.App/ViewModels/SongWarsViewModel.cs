using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using Spectralis.App.Services;
using Spectralis.Core.SongWars;

namespace Spectralis.App.ViewModels;

/// <summary>Tournaments belong to the session, never to a view or pop-out window.</summary>
public sealed class SongWarsViewModel : ViewModelBase
{
    private readonly SongWarsTournamentStore store;
    private readonly SemaphoreSlim operations = new(1, 1);
    private SongWarsSessionController? session;
    private string name = "Song Wars", judgeNames = "Host", status = "", savedId = "";
    private bool busy;

    public SongWarsViewModel(AppSettings settings) : this(new SongWarsTournamentStore()) { }
    internal SongWarsViewModel(SongWarsTournamentStore store)
    {
        this.store = store;
        CreateCommand = Command(CreateAsync);
        LoadCommand = Command(LoadAsync);
        PlayACommand = Command(() => PlayAsync(SongWarsMatchSlot.A));
        PlayBCommand = Command(() => PlayAsync(SongWarsMatchSlot.B));
        VoteCommand = Command(async () => { RequireSession().OpenPrimaryVoting(); await SaveAsync(); });
        EliminationCommand = Command(async () => { RequireSession().OpenEliminationVoting(); await SaveAsync(); });
        RevealCommand = Command(RevealAsync);
        SkipCommand = Command(async () => { RequireSession().Skip(); await SaveAsync(); await ReleaseAsync(); });
        PauseCommand = Command(async () => { RequireSession().Pause(); SetRoundPaused?.Invoke(true); await SaveAsync(); });
        ResumeCommand = Command(async () => { RequireSession().Resume(); SetRoundPaused?.Invoke(false); await SaveAsync(); });
        ReturnToQueueCommand = Command(ReleaseAsync);
        CloseCommand = Command(async () => { await ReleaseAsync(); session = null; Judges.Clear(); });
        RefreshSaved();
    }

    public Func<string, Task>? PlayRoundTrack { get; set; }
    public Func<Task>? EndRound { get; set; }
    public Action<bool>? SetRoundPaused { get; set; }
    public event Action? TournamentChanged;
    public SongWarsSessionController? Session => session;
    public SongWarsTournament? Tournament => session?.Tournament;
    public bool HasTournament => session is not null;
    public bool IsBusy { get => busy; private set => this.RaiseAndSetIfChanged(ref busy, value); }
    public string Name { get => name; set => this.RaiseAndSetIfChanged(ref name, value); }
    public string JudgeNames { get => judgeNames; set => this.RaiseAndSetIfChanged(ref judgeNames, value); }
    public string Status { get => status; private set => this.RaiseAndSetIfChanged(ref status, value); }
    public string SavedId { get => savedId; set => this.RaiseAndSetIfChanged(ref savedId, value); }
    public string TournamentName => Tournament?.Name ?? "Song Wars";
    public string TrackA => session?.CurrentTrackA?.DisplayTitle ?? "No match";
    public string TrackB => session?.CurrentTrackB?.DisplayTitle ?? "No match";
    public string ArtistA => session?.CurrentTrackA?.ArtistDisplayName ?? "";
    public string ArtistB => session?.CurrentTrackB?.ArtistDisplayName ?? "";
    public string Round => session?.CurrentMatch is { } match ? $"{match.Bracket} · {match.RoundId}" : "Tournament complete";
    public string Phase => session?.CurrentMatch?.Phase switch
    {
        SongWarsMatchPhase.TrackAPlaying => "Playing A", SongWarsMatchPhase.TrackBPlaying => "Playing B",
        SongWarsMatchPhase.PrimaryVoting => "Voting open", SongWarsMatchPhase.EliminationVoting => "Elimination vote",
        SongWarsMatchPhase.Paused => "Paused", _ => "Ready for the next match"
    };
    public bool CanPlayA => session?.CurrentMatch?.Phase is SongWarsMatchPhase.Pending or SongWarsMatchPhase.Ready or SongWarsMatchPhase.TrackAPlaying;
    public bool CanPlayB => session?.CurrentMatch?.Phase is SongWarsMatchPhase.TrackAPlaying or SongWarsMatchPhase.TrackBPlaying;
    public bool CanOpenVoting => session?.CurrentMatch?.Phase == SongWarsMatchPhase.TrackBPlaying;
    public bool CanVote => session?.CurrentMatch?.Phase is SongWarsMatchPhase.PrimaryVoting or SongWarsMatchPhase.EliminationVoting;
    public bool IsPaused => session?.CurrentMatch?.Phase == SongWarsMatchPhase.Paused;
    public bool CanEliminate => session?.CurrentMatch?.Bracket == SongWarsBracket.Winners && CanVote;
    public string Tally => session?.CurrentMatch is null ? "" : $"{session.TallyCurrentLive().SubmittedJudgeCount} / {Tournament!.Judges.Count} judges voted";
    public ObservableCollection<SongWarsSubmission> Entries { get; } = [];
    public ObservableCollection<SongWarsJudgeViewModel> Judges { get; } = [];
    public ObservableCollection<string> SavedTournaments { get; } = [];
    public ObservableCollection<string> Results { get; } = [];
    public ReactiveCommand<Unit, Unit> CreateCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadCommand { get; }
    public ReactiveCommand<Unit, Unit> PlayACommand { get; }
    public ReactiveCommand<Unit, Unit> PlayBCommand { get; }
    public ReactiveCommand<Unit, Unit> VoteCommand { get; }
    public ReactiveCommand<Unit, Unit> EliminationCommand { get; }
    public ReactiveCommand<Unit, Unit> RevealCommand { get; }
    public ReactiveCommand<Unit, Unit> SkipCommand { get; }
    public ReactiveCommand<Unit, Unit> PauseCommand { get; }
    public ReactiveCommand<Unit, Unit> ResumeCommand { get; }
    public ReactiveCommand<Unit, Unit> ReturnToQueueCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public void AddEntry(string title, string? artist, string source)
    {
        if (Entries.Any(e => e.LocalFilePath == source || e.ExternalSource == source)) return;
        Entries.Add(new SongWarsSubmission
        {
            SubmissionId = Guid.NewGuid().ToString("N"), DisplayTitle = title,
            ArtistDisplayName = artist ?? "", LocalFilePath = Path.IsPathFullyQualified(source) && File.Exists(source) ? source : null,
            ExternalSource = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? source : null
        });
        Status = $"{Entries.Count} tracks ready for a new tournament.";
    }

    private ReactiveCommand<Unit, Unit> Command(Func<Task> action) => ReactiveCommand.CreateFromTask(() => RunAsync(action), this.WhenAnyValue(x => x.IsBusy, b => !b));
    internal async Task RunAsync(Func<Task> action)
    {
        await operations.WaitAsync();
        IsBusy = true;
        try { Status = ""; await action(); }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; Refresh(); operations.Release(); }
    }

    private SongWarsSessionController RequireSession() => session ?? throw new InvalidOperationException("Create or load a tournament first.");
    private async Task CreateAsync()
    {
        if (HasTournament) throw new InvalidOperationException("Close the current tournament before creating another.");
        var names = JudgeNames.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        if (string.IsNullOrWhiteSpace(Name) || Entries.Count < 2 || names.Length is < 1 or > 5)
            throw new InvalidOperationException("Add a name, at least two tracks, and one to five judges.");
        var tournament = SongWarsBracketEngine.CreateTournament(Name.Trim(), Entries.ToList(), names.Select((n, i) => new SongWarsJudge
        {
            JudgeId = $"judge-{i + 1}", DisplayName = n, JoinToken = Guid.NewGuid().ToString("N")
        }).ToList());
        var next = new SongWarsSessionController(tournament);
        next.Start();
        await store.SaveTournamentAsync(tournament);
        session = next;
        Entries.Clear();
        SetJudges();
        RefreshSaved();
    }

    private async Task LoadAsync()
    {
        if (HasTournament) throw new InvalidOperationException("Close the current tournament before loading another.");
        var tournament = await store.LoadTournamentAsync(SavedId) ?? throw new InvalidOperationException("That tournament could not be loaded.");
        var next = new SongWarsSessionController(tournament);
        foreach (var match in tournament.Matches)
            next.LoadVotes(match.MatchId, await store.LoadVotesAsync(tournament.TournamentId, match.MatchId));
        session = next;
        SetJudges();
    }

    private void SetJudges()
    {
        Judges.Clear();
        foreach (var judge in Tournament!.Judges) Judges.Add(new SongWarsJudgeViewModel(judge.DisplayName,
            (slot, eliminate) => RunAsync(async () =>
            {
                var phase = session!.CurrentMatch?.Phase == SongWarsMatchPhase.EliminationVoting ? SongWarsVotePhase.Elimination : SongWarsVotePhase.Primary;
                if (eliminate) session.SubmitEliminationVote(judge.JudgeId, slot, phase);
                else session.SubmitTrackVote(judge.JudgeId, slot, SongWarsTrackVoteChoice.Pass, phase);
                await SaveAsync();
            })));
    }

    private async Task PlayAsync(SongWarsMatchSlot slot)
    {
        var current = RequireSession();
        if (slot == SongWarsMatchSlot.A && !CanPlayA || slot == SongWarsMatchSlot.B && !CanPlayB)
            throw new InvalidOperationException("Play A, then B, then open voting.");
        var track = slot == SongWarsMatchSlot.A ? current.CurrentTrackA : current.CurrentTrackB;
        var source = track?.LocalFilePath ?? track?.ExternalSource;
        if (string.IsNullOrWhiteSpace(source)) throw new InvalidOperationException("This entry has no playable source.");
        if (PlayRoundTrack is null) throw new InvalidOperationException("Session playback is unavailable.");
        await PlayRoundTrack(source);
        if (slot == SongWarsMatchSlot.A && current.CurrentMatch?.Phase != SongWarsMatchPhase.TrackAPlaying) current.BeginTrackA();
        if (slot == SongWarsMatchSlot.B && current.CurrentMatch?.Phase != SongWarsMatchPhase.TrackBPlaying) current.BeginTrackB();
        await SaveAsync();
    }

    private async Task RevealAsync()
    {
        var result = RequireSession().RevealCurrent(timerExpired: false);
        Status = result.Explanation;
        await SaveAsync();
        if (result.Outcome != SongWarsOutcome.Pending) await ReleaseAsync();
    }

    private async Task ReleaseAsync() { if (EndRound is not null) await EndRound(); }
    private async Task SaveAsync()
    {
        if (session is null) return;
        foreach (var match in session.Tournament.Matches)
            await store.SaveVotesAsync(session.Tournament.TournamentId, match.MatchId, session.GetVotes(match.MatchId).ToArray());
        await store.SaveTournamentAsync(session.Tournament);
    }
    private void RefreshSaved() { SavedTournaments.Clear(); foreach (var id in store.ListTournamentIds()) SavedTournaments.Add(id); }
    private void Refresh()
    {
        this.RaisePropertyChanged(nameof(IsPaused));
        foreach (var property in new[] { nameof(Session), nameof(Tournament), nameof(HasTournament), nameof(TournamentName), nameof(TrackA), nameof(TrackB), nameof(ArtistA), nameof(ArtistB), nameof(Round), nameof(Phase), nameof(CanPlayA), nameof(CanPlayB), nameof(CanOpenVoting), nameof(CanVote), nameof(CanEliminate), nameof(Tally) }) this.RaisePropertyChanged(property);
        Results.Clear();
        foreach (var match in Tournament?.Matches.Where(m => m.Result != SongWarsOutcome.Pending) ?? [])
        {
            var winner = Tournament!.Submissions.FirstOrDefault(s => s.SubmissionId == match.WinnerSubmissionId)?.DisplayTitle;
            Results.Add($"{match.RoundId} · {winner ?? match.Result.ToString()}");
        }
        TournamentChanged?.Invoke();
    }
}

public sealed class SongWarsJudgeViewModel : ViewModelBase
{
    public SongWarsJudgeViewModel(string name, Func<SongWarsMatchSlot, bool, Task> vote)
    {
        Name = name;
        VoteACommand = ReactiveCommand.CreateFromTask(() => vote(SongWarsMatchSlot.A, false));
        VoteBCommand = ReactiveCommand.CreateFromTask(() => vote(SongWarsMatchSlot.B, false));
        EliminateACommand = ReactiveCommand.CreateFromTask(() => vote(SongWarsMatchSlot.A, true));
        EliminateBCommand = ReactiveCommand.CreateFromTask(() => vote(SongWarsMatchSlot.B, true));
    }
    public string Name { get; }
    public ReactiveCommand<Unit, Unit> VoteACommand { get; }
    public ReactiveCommand<Unit, Unit> VoteBCommand { get; }
    public ReactiveCommand<Unit, Unit> EliminateACommand { get; }
    public ReactiveCommand<Unit, Unit> EliminateBCommand { get; }
}
