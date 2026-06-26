using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Spectralis.Core.SongWars;

namespace Spectralis.App.Views;

public partial class SongWarsWindow : Window
{
    private readonly SongWarsTournamentStore _store = new();
    private SongWarsSessionController? _session;

    private readonly List<TextBox> _judgeBoxes = [];
    private readonly List<SongWarsSubmissionEntry> _submissionEntries = [];
    private readonly List<JudgeVoteRow> _judgeVoteRows = [];

    public Action<string>? RequestPlay { get; set; }

    /// <summary>The active tournament, if a session is in progress. Consumed by OBS overlay push.</summary>
    public SongWarsTournament? CurrentTournament => _session?.Tournament;

    public SongWarsWindow()
    {
        InitializeComponent();
        BuildSetupJudgeBoxes();
        RefreshTournamentList();
        ShowPanel(BrowserPanel);
        MarkTab(TabBrowser);
    }

    // ─── Tab navigation ───────────────────────────────────────────────────────

    private void OnTabBrowser(object? sender, RoutedEventArgs e) { ShowPanel(BrowserPanel); MarkTab(TabBrowser); }
    private void OnTabSetup(object? sender, RoutedEventArgs e)   { ShowPanel(SetupPanel);  MarkTab(TabSetup); }

    private void OnTabHost(object? sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        ShowPanel(HostPanel);
        MarkTab(TabHost);
        RefreshHostPanel();
    }

    private void ShowPanel(Control panel)
    {
        BrowserPanel.IsVisible = false;
        SetupPanel.IsVisible   = false;
        HostPanel.IsVisible    = false;
        panel.IsVisible        = true;
    }

    private void MarkTab(Button active)
    {
        TabBrowser.Classes.Set("accent", TabBrowser == active);
        TabSetup  .Classes.Set("accent", TabSetup   == active);
        TabHost   .Classes.Set("accent", TabHost     == active);
    }

    private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Escape) Close();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    // ─── Browser panel ────────────────────────────────────────────────────────

    private void RefreshTournamentList()
    {
        var ids = _store.ListTournamentIds();
        TournamentList.ItemsSource = ids;
        FooterStatus.Text = $"{ids.Count} saved tournament(s)";
    }

    private void OnTournamentSelected(object? sender, SelectionChangedEventArgs e)
    {
        var hasSelection = TournamentList.SelectedItem is not null;
        LoadTournamentButton.IsEnabled   = hasSelection;
        DeleteTournamentButton.IsEnabled = hasSelection;
    }

    private async void OnLoadTournament(object? sender, RoutedEventArgs e)
    {
        if (TournamentList.SelectedItem is not string id) return;
        var tournament = await _store.LoadTournamentAsync(id);
        if (tournament is null) { FooterStatus.Text = "Failed to load tournament."; return; }

        _session = new SongWarsSessionController(tournament);
        TabHost.IsEnabled = true;
        BuildJudgeVoteRows();
        ShowPanel(HostPanel);
        MarkTab(TabHost);
        RefreshHostPanel();
    }

    private void OnDeleteTournament(object? sender, RoutedEventArgs e)
    {
        if (TournamentList.SelectedItem is not string id) return;
        _store.DeleteTournament(id);
        RefreshTournamentList();
    }

    // ─── Setup panel ──────────────────────────────────────────────────────────

    private void BuildSetupJudgeBoxes()
    {
        JudgeNamesPanel.Children.Clear();
        _judgeBoxes.Clear();
        for (var i = 0; i < 5; i++)
        {
            var box = new TextBox { Watermark = $"Judge {i + 1} name" };
            _judgeBoxes.Add(box);
            JudgeNamesPanel.Children.Add(box);
        }
    }

    private void OnSubmissionSelected(object? sender, SelectionChangedEventArgs e)
    {
        RemoveSubButton.IsEnabled = SubmissionList.SelectedIndex >= 0;
    }

    private async void OnAddSubmission(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Audio Files") { Patterns = ["*.mp3", "*.flac", "*.wav", "*.ogg", "*.m4a", "*.aac"] }]
        });

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) continue;
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            _submissionEntries.Add(new SongWarsSubmissionEntry(name, "", path));
        }
        RefreshSubmissionList();
    }

    private void OnRemoveSubmission(object? sender, RoutedEventArgs e)
    {
        var idx = SubmissionList.SelectedIndex;
        if (idx < 0 || idx >= _submissionEntries.Count) return;
        _submissionEntries.RemoveAt(idx);
        RefreshSubmissionList();
    }

    private void RefreshSubmissionList()
    {
        SubmissionList.ItemsSource = null;
        SubmissionList.ItemsSource = _submissionEntries.Select(s => s.DisplayTitle).ToList();
        SubCountLabel.Text = $"({_submissionEntries.Count})";
    }

    private async void OnCreateTournament(object? sender, RoutedEventArgs e)
    {
        var name = TournamentNameBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
        {
            SetupStatusLabel.Text = "Please enter a tournament name.";
            return;
        }
        if (_submissionEntries.Count < 2)
        {
            SetupStatusLabel.Text = "Add at least 2 tracks.";
            return;
        }

        var judgeNames = _judgeBoxes
            .Select(b => b.Text?.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList();

        if (judgeNames.Count == 0)
        {
            SetupStatusLabel.Text = "Add at least 1 judge.";
            return;
        }

        var tournament = SongWarsBracketEngine.CreateTournament(
            name,
            _submissionEntries.Select(s => new SongWarsSubmission
            {
                SubmissionId     = Guid.NewGuid().ToString("N"),
                DisplayTitle     = s.DisplayTitle,
                ArtistDisplayName = s.Artist,
                LocalFilePath    = s.FilePath
            }).ToList(),
            judgeNames.Select((n, i) => new SongWarsJudge
            {
                JudgeId     = $"judge-{i + 1}",
                DisplayName = n,
                JoinToken   = Guid.NewGuid().ToString("N")[..8]
            }).ToList()
        );

        SongWarsBracketEngine.StartTournament(tournament);
        await _store.SaveTournamentAsync(tournament);

        _session = new SongWarsSessionController(tournament);
        TabHost.IsEnabled = true;
        BuildJudgeVoteRows();
        RefreshTournamentList();

        ShowPanel(HostPanel);
        MarkTab(TabHost);
        RefreshHostPanel();
    }

    // ─── Host panel ──────────────────────────────────────────────────────────

    private void BuildJudgeVoteRows()
    {
        JudgeVoteRows.Children.Clear();
        _judgeVoteRows.Clear();

        if (_session is null) return;

        foreach (var judge in _session.Tournament.Judges)
        {
            var row = new JudgeVoteRow(judge, OnJudgeVote);
            _judgeVoteRows.Add(row);
            JudgeVoteRows.Children.Add(row.Root);
        }
    }

    private void OnJudgeVote(SongWarsJudge judge, SongWarsVoteChoice choice)
    {
        if (_session is null) return;
        try
        {
            _session.SubmitVote(judge.JudgeId, choice);
            RefreshTallyLabel();
            AutoSave();
        }
        catch (InvalidOperationException ex)
        {
            MatchStatusLabel.Text = ex.Message;
        }
    }

    private void RefreshTallyLabel()
    {
        if (_session is null) return;
        var tally = _session.TallyCurrentLive();
        TallyLabel.Text = $"Live tally — Pass: {tally.PassCount}  Fail: {tally.FailCount}  Eliminated: {tally.EliminatedCount}  ({tally.SubmittedJudgeCount}/{_session.Tournament.Judges.Count} submitted)";
    }

    private void RefreshHostPanel()
    {
        if (_session is null) return;
        var t = _session.Tournament;

        TournamentHeaderLabel.Text = t.Name;

        var match = _session.CurrentMatch;
        if (match is null)
        {
            RoundStatusLabel.Text = "Tournament complete.";
            SetPhaseButtonStates(null);
            TrackATitle.Text = TrackAArtist.Text = TrackBTitle.Text = TrackBArtist.Text = "—";
            ResultSection.IsVisible = false;
            MatchStatusLabel.Text = "";
            return;
        }

        RoundStatusLabel.Text = $"{match.Bracket}  ·  {match.RoundId}  ·  Phase: {match.Phase}";

        var trackA = _session.CurrentTrackA;
        var trackB = _session.CurrentTrackB;
        TrackATitle.Text   = trackA?.DisplayTitle       ?? "—";
        TrackAArtist.Text  = trackA?.ArtistDisplayName  ?? "";
        TrackBTitle.Text   = trackB?.DisplayTitle       ?? "—";
        TrackBArtist.Text  = trackB?.ArtistDisplayName  ?? "";

        PlayAButton.IsEnabled = !string.IsNullOrWhiteSpace(trackA?.LocalFilePath);
        PlayBButton.IsEnabled = !string.IsNullOrWhiteSpace(trackB?.LocalFilePath);

        SetPhaseButtonStates(match.Phase);
        RefreshVoteRowStates(match.Phase);

        VoteSection.IsVisible = match.Phase is SongWarsMatchPhase.PrimaryVoting or SongWarsMatchPhase.EliminationVoting;

        if (match.Phase == SongWarsMatchPhase.Reveal && match.VoteSnapshots.LastOrDefault() is { } snap)
        {
            ResultSection.IsVisible = true;
            OutcomeTitleLabel.Text  = $"Result: {snap.Outcome}";
            OutcomeDetailLabel.Text = snap.Explanation;
        }
        else
        {
            ResultSection.IsVisible = false;
        }

        PauseResumeButton.Content = match.Phase == SongWarsMatchPhase.Paused ? "Resume" : "Pause";

        RefreshMatchLog();
        MatchStatusLabel.Text = "";
    }

    private void SetPhaseButtonStates(SongWarsMatchPhase? phase)
    {
        BeginAButton.IsEnabled     = phase is SongWarsMatchPhase.Pending or SongWarsMatchPhase.Ready;
        BeginBButton.IsEnabled     = phase is SongWarsMatchPhase.TrackAPlaying or SongWarsMatchPhase.Paused;
        OpenVotingButton.IsEnabled = phase is SongWarsMatchPhase.TrackBPlaying or SongWarsMatchPhase.Paused;
        RevealButton.IsEnabled     = phase is SongWarsMatchPhase.PrimaryVoting or SongWarsMatchPhase.EliminationVoting or SongWarsMatchPhase.Paused;
        NextMatchButton.IsEnabled  = phase is SongWarsMatchPhase.Reveal or SongWarsMatchPhase.Complete or SongWarsMatchPhase.Skipped;
        SkipButton.IsEnabled       = phase is not null && phase != SongWarsMatchPhase.Complete && phase != SongWarsMatchPhase.Skipped && phase != SongWarsMatchPhase.Reveal;
        PauseResumeButton.IsEnabled = phase is not null && phase != SongWarsMatchPhase.Complete && phase != SongWarsMatchPhase.Skipped && phase != SongWarsMatchPhase.Reveal;
    }

    private void RefreshVoteRowStates(SongWarsMatchPhase phase)
    {
        var voting = phase is SongWarsMatchPhase.PrimaryVoting or SongWarsMatchPhase.EliminationVoting;
        var inWinners = _session?.CurrentMatch?.Bracket == SongWarsBracket.Winners;
        foreach (var row in _judgeVoteRows)
            row.SetEnabled(voting, inWinners);
    }

    private void RefreshMatchLog()
    {
        if (_session is null) return;
        var entries = _session.Tournament.AuditLog
            .OrderByDescending(a => a.AtUtc)
            .Take(50)
            .Select(a => $"[{a.AtUtc:HH:mm:ss}] {a.Message}")
            .ToList();
        MatchLog.ItemsSource = entries;
    }

    private void OnPlayA(object? sender, RoutedEventArgs e)
    {
        var path = _session?.CurrentTrackA?.LocalFilePath;
        if (!string.IsNullOrWhiteSpace(path)) RequestPlay?.Invoke(path);
    }

    private void OnPlayB(object? sender, RoutedEventArgs e)
    {
        var path = _session?.CurrentTrackB?.LocalFilePath;
        if (!string.IsNullOrWhiteSpace(path)) RequestPlay?.Invoke(path);
    }

    private void OnBeginA(object? sender, RoutedEventArgs e) => TryTransition(() => _session!.BeginTrackA(), "Begin A");
    private void OnBeginB(object? sender, RoutedEventArgs e) => TryTransition(() => _session!.BeginTrackB(), "Begin B");

    private void OnOpenVoting(object? sender, RoutedEventArgs e)
    {
        if (_session?.CurrentMatch?.Phase is SongWarsMatchPhase.EliminationVoting)
            TryTransition(() => _session.OpenEliminationVoting(), "Open Elim. Voting");
        else
            TryTransition(() => _session!.OpenPrimaryVoting(), "Open Voting");
    }

    private async void OnReveal(object? sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        try
        {
            var result = _session.RevealCurrent(timerExpired: true);
            ResultSection.IsVisible = true;
            OutcomeTitleLabel.Text  = $"Result: {result.Outcome}";
            OutcomeDetailLabel.Text = result.Explanation;
            await AutoSaveAsync();
            RefreshHostPanel();
        }
        catch (InvalidOperationException ex) { MatchStatusLabel.Text = ex.Message; }
    }

    private void OnNextMatch(object? sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        ResultSection.IsVisible = false;
        foreach (var row in _judgeVoteRows) row.ClearSelection();
        TallyLabel.Text = "";
        RefreshHostPanel();
    }

    private void OnSkip(object? sender, RoutedEventArgs e)
    {
        if (_session?.CurrentMatch is not { } match) return;
        try
        {
            match.Phase = SongWarsMatchPhase.Skipped;
            _session.Tournament.AuditLog.Add(new SongWarsAuditEntry { Kind = "skipped", Message = "Match skipped by host.", MatchId = match.MatchId, AtUtc = DateTimeOffset.UtcNow });
            AutoSave();
            RefreshHostPanel();
        }
        catch (Exception ex) { MatchStatusLabel.Text = ex.Message; }
    }

    private void OnPauseResume(object? sender, RoutedEventArgs e)
    {
        if (_session?.CurrentMatch?.Phase == SongWarsMatchPhase.Paused)
            TryTransition(() => _session.Resume(), "Resume");
        else
            TryTransition(() => _session!.Pause(), "Pause");
    }

    private void TryTransition(Action action, string label)
    {
        if (_session is null) return;
        try { action(); AutoSave(); RefreshHostPanel(); }
        catch (InvalidOperationException ex) { MatchStatusLabel.Text = $"{label}: {ex.Message}"; }
    }

    // ─── Persistence ──────────────────────────────────────────────────────────

    private void AutoSave()
    {
        if (_session is null) return;
        _ = _store.SaveTournamentAsync(_session.Tournament);
    }

    private async Task AutoSaveAsync()
    {
        if (_session is null) return;
        await _store.SaveTournamentAsync(_session.Tournament);
    }

    // ─── Inner types ─────────────────────────────────────────────────────────

    private sealed record SongWarsSubmissionEntry(string DisplayTitle, string Artist, string? FilePath);

    private sealed class JudgeVoteRow
    {
        private readonly SongWarsJudge _judge;
        private readonly Action<SongWarsJudge, SongWarsVoteChoice> _onVote;
        private readonly Button _passBtn;
        private readonly Button _failBtn;
        private readonly Button _elimBtn;

        public Panel Root { get; }

        public JudgeVoteRow(SongWarsJudge judge, Action<SongWarsJudge, SongWarsVoteChoice> onVote)
        {
            _judge  = judge;
            _onVote = onVote;

            _passBtn = MakeVoteBtn("Pass", SongWarsVoteChoice.Pass);
            _failBtn = MakeVoteBtn("Fail", SongWarsVoteChoice.Fail);
            _elimBtn = MakeVoteBtn("Elim", SongWarsVoteChoice.Eliminated);

            Root = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = judge.DisplayName, Width = 90, VerticalAlignment = VerticalAlignment.Center },
                    _passBtn, _failBtn, _elimBtn
                }
            };
        }

        private Button MakeVoteBtn(string label, SongWarsVoteChoice choice)
        {
            var btn = new Button { Content = label, IsEnabled = false };
            btn.Click += (_, _) =>
            {
                _passBtn.Classes.Remove("accent");
                _failBtn.Classes.Remove("accent");
                _elimBtn.Classes.Remove("accent");
                btn.Classes.Add("accent");
                _onVote(_judge, choice);
            };
            return btn;
        }

        public void SetEnabled(bool votingOpen, bool inWinners)
        {
            _passBtn.IsEnabled = votingOpen;
            _failBtn.IsEnabled = votingOpen;
            _elimBtn.IsEnabled = votingOpen && inWinners;
        }

        public void ClearSelection()
        {
            _passBtn.Classes.Remove("accent");
            _failBtn.Classes.Remove("accent");
            _elimBtn.Classes.Remove("accent");
        }
    }
}
