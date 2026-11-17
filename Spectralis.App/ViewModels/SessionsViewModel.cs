using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using Spectralis.App.Services;
using Spectralis.Core.Sessions;

namespace Spectralis.App.ViewModels;

/// <summary>The workspace owns its lifecycle and playback policy; transports only supply room data.</summary>
public sealed class SessionsViewModel : ViewModelBase, IDisposable
{
    private bool active, busy;
    private bool refreshQueued;
    private readonly HashSet<string> approvedListeners = [];
    private string status = "", joinCode = "";
    public SessionsViewModel(AppSettings settings, SharedPlayViewModel listening, StreamerQueueViewModel requests,
        SongWarsViewModel competition, NowPlayingViewModel nowPlaying, Func<string, Task> play, Func<string, Task> enqueue)
    {
        Listening = listening;
        Player = nowPlaying;
        listening.AutoQueueListenerRequests = false;
        Requests = requests;
        Competition = competition;
        Account = new WardAccountViewModel(settings);
        Playback = new SessionPlaybackCoordinator(nowPlaying.SuspendSessionQueueAsync, nowPlaying.RestoreSessionQueueAsync, play, enqueue);
        Playback.Changed += OnPlaybackChanged;
        listening.PlayTrackRequested = async source => { await Playback.RequestAsync(source, true); };
        listening.QueueTrackRequested = async source => { await Playback.RequestAsync(source, false); };
        requests.SessionPlayRequested = source => Playback.RequestAsync(source, true);
        competition.PlayRoundTrack = async source =>
        {
            if (listening.IsJoined) throw new InvalidOperationException("Leave the session you're listening to before hosting Song Wars.");
            await Playback.PlayRoundTrackAsync(source);
        };
        competition.EndRound = Playback.EndRoundAsync;
        competition.SetRoundPaused = paused =>
        {
            if (Playback.RoundOwnsPlayback && nowPlaying.IsPlaying == paused) nowPlaying.TogglePlayback();
        };
        competition.TournamentChanged += () => { nowPlaying.SongWarsSession = competition.Session; nowPlaying.NotifySongWarsChanged(); };
        requests.SettingsSaveRequested += AppSettingsStore.Save;
        StartCommand = Command(async () =>
        {
            if (Listening.IsJoined) throw new InvalidOperationException("Leave the current session before hosting.");
            // Request intake and audio hosting are independent: a temporary outage in one must not block the other.
            await Listening.HostCommand.Execute();
            Active = true;
            await Requests.StartSessionAsync();
        });
        StopCommand = Command(async () =>
        {
            await Playback.EndRoundAsync();
            try { await Requests.EndSessionAsync(); }
            catch (Exception ex) { Status = $"Listening stopped, but request intake could not be closed: {ex.Message}"; }
            finally { await Listening.StopCommand.Execute(); Active = false; }
        });
        JoinCommand = Command(async () =>
        {
            if (Active || Playback.RoundOwnsPlayback) throw new InvalidOperationException("End your session before joining another.");
            if (string.IsNullOrWhiteSpace(JoinCode)) throw new InvalidOperationException("Enter a room code.");
            await Listening.JoinAsync(JoinCode.Trim(), settings.SharedPlayCdnBaseUrl);
        });
        RetryRequestsCommand = Command(() => Requests.StartSessionAsync());
        Requests.QueueItems.CollectionChanged += OnRequestsChanged;
        Requests.PendingItems.CollectionChanged += OnRequestsChanged;
        Listening.RequestedTracks.CollectionChanged += OnRequestsChanged;
        Listening.PropertyChanged += OnListeningChanged;
        Requests.PropertyChanged += OnRequestSettingsChanged;
        Active = listening.IsHosting || listening.HostPending;
        RefreshRequests();
    }

    public SharedPlayViewModel Listening { get; }
    public NowPlayingViewModel Player { get; }
    public StreamerQueueViewModel Requests { get; }
    public SongWarsViewModel Competition { get; }
    public WardAccountViewModel Account { get; }
    public SessionPlaybackCoordinator Playback { get; }
    public ObservableCollection<SessionRequestViewModel> Inbox { get; } = [];
    public bool HasRequests => Inbox.Count > 0;
    public bool Active { get => active; private set => this.RaiseAndSetIfChanged(ref active, value); }
    public bool Busy { get => busy; private set => this.RaiseAndSetIfChanged(ref busy, value); }
    public string Status { get => status; private set => this.RaiseAndSetIfChanged(ref status, value); }
    public string JoinCode { get => joinCode; set => this.RaiseAndSetIfChanged(ref joinCode, value); }
    public bool RoundActive => Playback.RoundOwnsPlayback;
    public string PlaybackStatus => RoundActive ? $"Song Wars has playback · {Playback.WaitingCount} waiting" : "Listening queue has playback";
    public ReactiveCommand<Unit, Unit> StartCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> JoinCommand { get; }
    public ReactiveCommand<Unit, Unit> RetryRequestsCommand { get; }

    private ReactiveCommand<Unit, Unit> Command(Func<Task> work) => ReactiveCommand.CreateFromTask(async () =>
    {
        Busy = true;
        Status = "";
        try { await work(); }
        catch (Exception ex) { Status = ex.Message; }
        finally { Busy = false; }
    }, this.WhenAnyValue(x => x.Busy, b => !b));
    private void OnPlaybackChanged()
    {
        this.RaisePropertyChanged(nameof(RoundActive));
        this.RaisePropertyChanged(nameof(PlaybackStatus));
    }
    private void OnListeningChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(Listening.IsHosting) or nameof(Listening.HostPending))
            Active = Listening.IsHosting || Listening.HostPending;
    }
    private void OnRequestSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Requests.RequireApproval)) RefreshRequests();
    }
    private void OnRequestsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (refreshQueued) return;
        refreshQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { refreshQueued = false; RefreshRequests(); });
    }
    private void RefreshRequests()
    {
        // Keep row identity while transports refresh so selection and keyboard focus survive polling.
        var next = Requests.PendingItems.Concat(Requests.QueueItems).DistinctBy(i => i.Id).Select(item =>
            new SessionRequestViewModel("request:" + item.Id, item.Title, item.Artist, item.DisplayName,
                item.IsPending, item.PlayCommand, item.ApproveCommand, item.RejectCommand,
                () => AddToCompetition(item.Title, item.Artist, Requests.ResolvePlaybackUrl(item))))
            .Concat(Listening.RequestedTracks.Select(item => new SessionRequestViewModel("listener:" + item.Id,
                item.Title, item.Artist, item.AddedBy, Requests.RequireApproval && !approvedListeners.Contains(item.Id), item.PlayCommand,
                ReactiveCommand.Create(() => { approvedListeners.Add(item.Id); RefreshRequests(); }), item.RemoveCommand,
                () => AddToCompetition(item.Title, item.Artist, item.Url)))).ToList();
        var ids = next.Select(i => i.Id).ToHashSet();
        foreach (var row in Inbox.Where(i => !ids.Contains(i.Id)).ToList()) Inbox.Remove(row);
        for (var index = 0; index < next.Count; index++)
        {
            var row = next[index];
            var existing = Inbox.FirstOrDefault(i => i.Id == row.Id);
            if (existing is null) Inbox.Insert(index, row);
            else if (existing.Title != row.Title || existing.Pending != row.Pending) { Inbox.Remove(existing); Inbox.Insert(index, row); }
            else if (Inbox.IndexOf(existing) != index) Inbox.Move(Inbox.IndexOf(existing), index);
        }
        this.RaisePropertyChanged(nameof(HasRequests));
    }
    private void AddToCompetition(string title, string? artist, string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) { Status = "This request has no playable source."; return; }
        Competition.AddEntry(title, artist, source);
        Status = $"Added {title} to the next tournament.";
    }
    public void Dispose()
    {
        Playback.Changed -= OnPlaybackChanged;
        Requests.QueueItems.CollectionChanged -= OnRequestsChanged;
        Requests.PendingItems.CollectionChanged -= OnRequestsChanged;
        Listening.RequestedTracks.CollectionChanged -= OnRequestsChanged;
        Listening.PropertyChanged -= OnListeningChanged;
        Requests.PropertyChanged -= OnRequestSettingsChanged;
        Account.Dispose();
        Requests.SettingsSaveRequested -= AppSettingsStore.Save;
    }
}

public sealed class SessionRequestViewModel : ViewModelBase
{
    public SessionRequestViewModel(string id, string title, string? artist, string sender, bool pending,
        ReactiveCommand<Unit, Unit> play, ReactiveCommand<Unit, Unit>? approve, ReactiveCommand<Unit, Unit> remove, Action compete)
    {
        Id = id; Title = title; Artist = artist ?? ""; Sender = sender; Pending = pending;
        PlayCommand = play; ApproveCommand = approve; RemoveCommand = remove;
        CompeteCommand = ReactiveCommand.Create(compete);
    }
    public string Id { get; }
    public string Title { get; }
    public string Artist { get; }
    public string Sender { get; }
    public bool Pending { get; }
    public ReactiveCommand<Unit, Unit> PlayCommand { get; }
    public ReactiveCommand<Unit, Unit>? ApproveCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
    public ReactiveCommand<Unit, Unit> CompeteCommand { get; }
}
