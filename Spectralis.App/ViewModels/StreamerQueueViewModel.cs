using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using Spectralis.App.Services;
using Spectralis.Core.Metadata;
using Spectralis.Core.StreamerQueue;

namespace Spectralis.App.ViewModels;

// ── Queue item VM used for the draggable list ─────────────────────────────────

public sealed class SqItemVm : ViewModelBase
{
    private bool _isPending;
    private string _title;
    private string? _artist;

    public SqItemVm(SqSubmission sub,
        string? channelName,
        Action<string> onApprove,
        Action<string> onReject,
        Action<string> onDelete,
        Action<SqItemVm> onMarkPlaying,
        Action<string, SqStatus> onSetStatus)
    {
        Id = sub.Id;
        DisplayName = sub.DisplayName;
        _title = sub.Title ?? "(untitled)";
        _artist = sub.Artist;
        Tier = sub.Tier;
        Status = sub.Status;
        DurationSeconds = sub.DurationSeconds;
        SourceKind = sub.SourceKind;
        Url = sub.Url;
        FileId = sub.FileId;
        FileName = sub.FileName;
        QueueChannelId = sub.QueueChannelId;
        ChannelName = channelName;
        _isPending = sub.Status == SqStatus.Pending;

        ApproveCommand      = ReactiveCommand.Create(() => onApprove(Id), this.WhenAnyValue(x => x.IsPending));
        RejectCommand       = ReactiveCommand.Create(() => onReject(Id));
        DeleteCommand       = ReactiveCommand.Create(() => onDelete(Id));
        PlayCommand         = ReactiveCommand.Create(() => onMarkPlaying(this));
        MarkPlayedCommand   = ReactiveCommand.Create(() => onSetStatus(Id, SqStatus.Played));
        MarkSkippedCommand  = ReactiveCommand.Create(() => onSetStatus(Id, SqStatus.Skipped));
        MarkNotPlayedCommand = ReactiveCommand.Create(() => onSetStatus(Id, SqStatus.Queued));
    }

    public string Id { get; }
    public string DisplayName { get; }

    /// <summary>Settable so a background metadata scrape (see
    /// StreamerQueueViewModel.ScrapeMetadataAsync) can fill in an accurate title/artist
    /// after the item is already on screen, instead of waiting for the next poll tick.</summary>
    public string Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public string? Artist
    {
        get => _artist;
        set => this.RaiseAndSetIfChanged(ref _artist, value);
    }

    public SqTier Tier { get; }
    public SqStatus Status { get; }
    public double? DurationSeconds { get; }
    public string SourceKind { get; }
    public string? Url { get; }
    public string? FileId { get; }
    public string? FileName { get; }
    public string? QueueChannelId { get; }

    /// <summary>Resolved from the room's channel list at construction time — null once the
    /// channel that produced this submission no longer exists (e.g. streamer deleted it).</summary>
    public string? ChannelName { get; }
    public bool HasChannelBadge => !string.IsNullOrWhiteSpace(ChannelName);

    public bool IsPending
    {
        get => _isPending;
        private set => this.RaiseAndSetIfChanged(ref _isPending, value);
    }

    public string TierLabel => Tier switch
    {
        SqTier.SuperSkip => "Super Skip",
        SqTier.Skip => "Skip",
        _ => string.Empty
    };

    public bool HasTierBadge => Tier != SqTier.Normal;

    public bool IsPlayed => Status == SqStatus.Played;
    public bool IsSkipped => Status == SqStatus.Skipped;
    public string StatusLabel => Status switch
    {
        SqStatus.Played => "Played",
        SqStatus.Skipped => "Skipped",
        _ => string.Empty
    };

    public ReactiveCommand<Unit, Unit> ApproveCommand { get; }
    public ReactiveCommand<Unit, Unit> RejectCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> PlayCommand { get; }
    public ReactiveCommand<Unit, Unit> MarkPlayedCommand { get; }
    public ReactiveCommand<Unit, Unit> MarkSkippedCommand { get; }
    public ReactiveCommand<Unit, Unit> MarkNotPlayedCommand { get; }
}

// ── Queue channel VM (settings panel row) ─────────────────────────────────────

/// <summary>One row in the "Queue Channels" settings list. Order IS priority — the top
/// row is both the highest-priority channel and the implicit default lane (links with
/// no ?ch=, and Discord posts from an unmapped channel, land there). Reorder with the
/// arrow buttons instead of typing a priority number; <see cref="StreamerQueueViewModel.BuildQueueChannels"/>
/// turns list position back into the numeric priority the backend expects.</summary>
public sealed class SqChannelVm : ViewModelBase
{
    private string _name;
    private string _discordChannelId;
    private string _shareUrl = string.Empty;

    public SqChannelVm(string id, string name, string? discordChannelId, Action<SqChannelVm> onRemove, Action<string> onCopyLink,
        Action<SqChannelVm> onMoveUp, Action<SqChannelVm> onMoveDown)
    {
        Id = id;
        _name = name;
        _discordChannelId = discordChannelId ?? string.Empty;

        RemoveCommand = ReactiveCommand.Create(() => onRemove(this));
        CopyLinkCommand = ReactiveCommand.Create(() => onCopyLink(ShareUrl), this.WhenAnyValue(x => x.ShareUrl, u => !string.IsNullOrEmpty(u)));
        MoveUpCommand = ReactiveCommand.Create(() => onMoveUp(this));
        MoveDownCommand = ReactiveCommand.Create(() => onMoveDown(this));
    }

    /// <summary>Slug used in the share URL (?ch=) and as the Discord pairing key. Fixed at
    /// creation — renaming is just the display Name, not the id, so existing links keep working.</summary>
    public string Id { get; }

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public string DiscordChannelId
    {
        get => _discordChannelId;
        set => this.RaiseAndSetIfChanged(ref _discordChannelId, value);
    }

    public string ShareUrl
    {
        get => _shareUrl;
        set => this.RaiseAndSetIfChanged(ref _shareUrl, value);
    }

    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyLinkCommand { get; }
    public ReactiveCommand<Unit, Unit> MoveUpCommand { get; }
    public ReactiveCommand<Unit, Unit> MoveDownCommand { get; }
}

// ── Mix pattern slot VM (settings panel row) ──────────────────────────────────

/// <summary>One slot in the repeating mix cycle (e.g. "2 x general", "3 x vip"). An empty
/// pattern means "no mixing" — channels just sort by Priority instead.</summary>
public sealed class SqMixSlotVm : ViewModelBase
{
    private string _channelId;
    private string _count;

    public SqMixSlotVm(string channelId, int count, Action<SqMixSlotVm> onRemove, Action<SqMixSlotVm> onMoveUp, Action<SqMixSlotVm> onMoveDown)
    {
        _channelId = channelId;
        _count = count.ToString();
        RemoveCommand = ReactiveCommand.Create(() => onRemove(this));
        MoveUpCommand = ReactiveCommand.Create(() => onMoveUp(this));
        MoveDownCommand = ReactiveCommand.Create(() => onMoveDown(this));
    }

    /// <summary>Must match an <see cref="SqChannelVm"/> id. Typed rather than picked from a
    /// dropdown — keeps this row independent of channel-list wiring.</summary>
    public string ChannelId
    {
        get => _channelId;
        set => this.RaiseAndSetIfChanged(ref _channelId, value);
    }

    public string Count
    {
        get => _count;
        set => this.RaiseAndSetIfChanged(ref _count, value);
    }

    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
    public ReactiveCommand<Unit, Unit> MoveUpCommand { get; }
    public ReactiveCommand<Unit, Unit> MoveDownCommand { get; }
}

// ── Main ViewModel ────────────────────────────────────────────────────────────

public sealed class StreamerQueueViewModel : ViewModelBase, IDisposable
{
    private readonly StreamerQueueRoomController _controller = new();
    private readonly OpenUrlService _openUrlService = new();
    private CancellationTokenSource _pollCts = new();

    // ── Metadata scraping ────────────────────────────────────────────────────────
    // Submissions from Discord (or anywhere else that doesn't run its own metadata
    // lookup) can land with a null title/artist. Rather than showing that forever,
    // resolve+download the source once in the background and cache the result here —
    // the backend has no title for this submission until someone edits it, so this
    // local cache is what makes the queue list show something accurate.
    private readonly Dictionary<string, (string? Title, string? Artist)> _scrapedMetadata = new();
    private readonly HashSet<string> _scrapeInFlight = new();
    private readonly SemaphoreSlim _scrapeGate = new(2);

    /// <summary>Channel id -> display name, refreshed every poll (unlike the editable
    /// <see cref="Channels"/> settings rows) purely so queue items can show a lane badge.</summary>
    private readonly Dictionary<string, string> _channelNames = new();

    // ── State ─────────────────────────────────────────────────────────────────
    private bool _hasRoom;
    private bool _isOwner;
    private string _roomId = string.Empty;
    private string _submitUrl = string.Empty;
    private string _statusText = "No queue configured";
    private string _lastError = string.Empty;
    private bool _isP2wActive;
    private string _addToQueueUrl = string.Empty;

    // ── Settings ──────────────────────────────────────────────────────────────
    private bool _sqEnabled;
    private bool _acceptingSubmissions = true;
    private bool _requireApproval;
    private bool _allowDuplicates;
    private bool _allowLinkSubmissions = true;
    private string _maxQueueLength = "50";
    private string _maxPerPerson = "2";
    private bool _skipBypassesLimit;
    private bool _queueFeeEnabled;
    private string _queueFeeAmount = "5.00";
    private bool _skipFeeEnabled;
    private string _skipFeeAmount = "2.00";
    private bool _superSkipFeeEnabled;
    private string _superSkipFeeAmount = "10.00";
    private bool _stripeConnected;
    private string _stripeStatus = "Not connected";
    private string _discordPinDisplay = string.Empty;
    private string _addToQueueChannelId = string.Empty;
    private int _channelSeq;

    private Uri _cdnBaseUri = new("https://audioplayer-production-5b83.up.railway.app");
    private AppSettings? _settings;

    public StreamerQueueViewModel()
    {
        var hasRoom = this.WhenAnyValue(x => x.HasRoom);
        var isOwner = this.WhenAnyValue(x => x.IsOwner);

        CreateRoomCommand      = ReactiveCommand.CreateFromTask(CreateRoomAsync);
        SaveSettingsCommand    = ReactiveCommand.CreateFromTask(SaveSettingsAsync, isOwner);
        CopySubmitUrlCommand   = ReactiveCommand.Create(CopySubmitUrl,
            this.WhenAnyValue(x => x.SubmitUrl, u => !string.IsNullOrEmpty(u)));
        ConnectStripeCommand   = ReactiveCommand.CreateFromTask(ConnectStripeAsync, isOwner);
        DisconnectStripeCommand = ReactiveCommand.CreateFromTask(DisconnectStripeAsync, isOwner);
        ClearNowPlayingCommand = ReactiveCommand.CreateFromTask(() => MarkNowPlayingAsync(null));
        ToggleAcceptingCommand = ReactiveCommand.CreateFromTask(ToggleAcceptingAsync, isOwner);
        LinkDiscordCommand     = ReactiveCommand.CreateFromTask(LinkDiscordAsync, isOwner);
        AddToQueueCommand      = ReactiveCommand.CreateFromTask(AddToQueueAsync,
            this.WhenAnyValue(x => x.AddToQueueUrl, x => x.IsOwner, (u, owner) => owner && !string.IsNullOrWhiteSpace(u)));
        AddChannelCommand      = ReactiveCommand.Create(AddChannel, isOwner);
        AddMixSlotCommand      = ReactiveCommand.Create(AddMixSlot,
            this.WhenAnyValue(x => x.IsOwner, x => x.Channels.Count, (owner, count) => owner && count > 0));
    }

    public void Dispose()
    {
        _pollCts.Cancel();
        _controller.Dispose();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public ReactiveCommand<Unit, Unit> CreateRoomCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> CopySubmitUrlCommand { get; }
    public ReactiveCommand<Unit, Unit> ConnectStripeCommand { get; }
    public ReactiveCommand<Unit, Unit> DisconnectStripeCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearNowPlayingCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleAcceptingCommand { get; }
    public ReactiveCommand<Unit, Unit> LinkDiscordCommand { get; }
    public ReactiveCommand<Unit, Unit> AddToQueueCommand { get; }
    public ReactiveCommand<Unit, Unit> AddChannelCommand { get; }
    public ReactiveCommand<Unit, Unit> AddMixSlotCommand { get; }

    public event Action<string>? CopyToClipboardRequested;
    public event Action<string>? OpenUrlRequested;

    /// <summary>Wired by MainWindowViewModel to NowPlaying.LoadUrlAsync so clicking Play actually plays the track.</summary>
    public Func<string, Task>? PlayTrackRequested { get; set; }
    public event Action<AppSettings>? SettingsSaveRequested;

    // ── Reactive properties ───────────────────────────────────────────────────

    public bool HasRoom
    {
        get => _hasRoom;
        private set => this.RaiseAndSetIfChanged(ref _hasRoom, value);
    }

    public bool IsOwner
    {
        get => _isOwner;
        private set => this.RaiseAndSetIfChanged(ref _isOwner, value);
    }

    public string RoomId
    {
        get => _roomId;
        private set => this.RaiseAndSetIfChanged(ref _roomId, value);
    }

    public string SubmitUrl
    {
        get => _submitUrl;
        private set => this.RaiseAndSetIfChanged(ref _submitUrl, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public string LastError
    {
        get => _lastError;
        private set => this.RaiseAndSetIfChanged(ref _lastError, value);
    }

    public bool IsP2wActive
    {
        get => _isP2wActive;
        private set => this.RaiseAndSetIfChanged(ref _isP2wActive, value);
    }

    public bool SqEnabled
    {
        get => _sqEnabled;
        set => this.RaiseAndSetIfChanged(ref _sqEnabled, value);
    }

    /// <summary>Whether the public page is currently taking new requests. Separate from
    /// <see cref="SqEnabled"/> — closing this still lets viewers see now-playing/queue and
    /// lets the streamer keep browsing and playing from the existing queue.</summary>
    public bool AcceptingSubmissions
    {
        get => _acceptingSubmissions;
        private set { this.RaiseAndSetIfChanged(ref _acceptingSubmissions, value); this.RaisePropertyChanged(nameof(AcceptingToggleLabel)); }
    }

    public string AcceptingToggleLabel => AcceptingSubmissions ? "Close Queue" : "Reopen Queue";

    public bool RequireApproval
    {
        get => _requireApproval;
        set => this.RaiseAndSetIfChanged(ref _requireApproval, value);
    }

    public bool AllowDuplicates
    {
        get => _allowDuplicates;
        set => this.RaiseAndSetIfChanged(ref _allowDuplicates, value);
    }

    public bool AllowLinkSubmissions
    {
        get => _allowLinkSubmissions;
        set => this.RaiseAndSetIfChanged(ref _allowLinkSubmissions, value);
    }

    public string MaxQueueLength
    {
        get => _maxQueueLength;
        set => this.RaiseAndSetIfChanged(ref _maxQueueLength, value);
    }

    public string MaxPerPerson
    {
        get => _maxPerPerson;
        set => this.RaiseAndSetIfChanged(ref _maxPerPerson, value);
    }

    public bool SkipBypassesLimit
    {
        get => _skipBypassesLimit;
        set => this.RaiseAndSetIfChanged(ref _skipBypassesLimit, value);
    }

    public bool QueueFeeEnabled
    {
        get => _queueFeeEnabled;
        set { this.RaiseAndSetIfChanged(ref _queueFeeEnabled, value); this.RaisePropertyChanged(nameof(AnyFeeEnabled)); }
    }

    public string QueueFeeAmount
    {
        get => _queueFeeAmount;
        set => this.RaiseAndSetIfChanged(ref _queueFeeAmount, value);
    }

    public bool SkipFeeEnabled
    {
        get => _skipFeeEnabled;
        set { this.RaiseAndSetIfChanged(ref _skipFeeEnabled, value); this.RaisePropertyChanged(nameof(AnyFeeEnabled)); }
    }

    public string SkipFeeAmount
    {
        get => _skipFeeAmount;
        set => this.RaiseAndSetIfChanged(ref _skipFeeAmount, value);
    }

    public bool SuperSkipFeeEnabled
    {
        get => _superSkipFeeEnabled;
        set { this.RaiseAndSetIfChanged(ref _superSkipFeeEnabled, value); this.RaisePropertyChanged(nameof(AnyFeeEnabled)); }
    }

    public string SuperSkipFeeAmount
    {
        get => _superSkipFeeAmount;
        set => this.RaiseAndSetIfChanged(ref _superSkipFeeAmount, value);
    }

    public bool AnyFeeEnabled => QueueFeeEnabled || SkipFeeEnabled || SuperSkipFeeEnabled;

    public bool StripeConnected
    {
        get => _stripeConnected;
        private set => this.RaiseAndSetIfChanged(ref _stripeConnected, value);
    }

    public string StripeStatus
    {
        get => _stripeStatus;
        private set => this.RaiseAndSetIfChanged(ref _stripeStatus, value);
    }

    /// <summary>Set after CreateDiscordPinAsync succeeds; shows the streamer the PIN to
    /// type into `/link-queue` in Discord. Cleared on room switch — a stale PIN read off
    /// screen after it expired is just confusing.</summary>
    public string DiscordPinDisplay
    {
        get => _discordPinDisplay;
        private set => this.RaiseAndSetIfChanged(ref _discordPinDisplay, value);
    }

    public string AddToQueueUrl
    {
        get => _addToQueueUrl;
        set => this.RaiseAndSetIfChanged(ref _addToQueueUrl, value);
    }

    /// <summary>Which queue channel a manual "Add to Queue" lands in. Empty means the
    /// room's default (first-listed) channel.</summary>
    public string AddToQueueChannelId
    {
        get => _addToQueueChannelId;
        set => this.RaiseAndSetIfChanged(ref _addToQueueChannelId, value);
    }

    public ObservableCollection<SqItemVm> QueueItems { get; } = [];
    public ObservableCollection<SqItemVm> PendingItems { get; } = [];
    public ObservableCollection<SqItemVm> HistoryItems { get; } = [];
    public SqItemVm? NowPlayingItem { get; private set; }

    /// <summary>Settings-panel rows for the room's queue channels. The first row is the
    /// implicit default lane (see <see cref="SqChannelVm"/>).</summary>
    public ObservableCollection<SqChannelVm> Channels { get; } = [];

    /// <summary>Settings-panel rows for the mix pattern. Empty means "no mixing" —
    /// channels just sort by <see cref="SqChannelVm.Priority"/>.</summary>
    public ObservableCollection<SqMixSlotVm> MixPattern { get; } = [];

    // ── Init ──────────────────────────────────────────────────────────────────

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        if (!string.IsNullOrWhiteSpace(settings.SqCdnBaseUrl))
            _cdnBaseUri = new Uri(settings.SqCdnBaseUrl);

        if (!string.IsNullOrWhiteSpace(settings.SqRoomId) && !string.IsNullOrWhiteSpace(settings.SqOwnerToken))
        {
            _controller.Configure(_cdnBaseUri, settings.SqRoomId, settings.SqOwnerToken);
            RoomId = settings.SqRoomId;
            HasRoom = true;
            IsOwner = true;
            UpdateSubmitUrl();
            StartPolling();
        }
    }

    // ── Room creation ─────────────────────────────────────────────────────────

    private async Task CreateRoomAsync(CancellationToken ct)
    {
        try
        {
            LastError = string.Empty;
            StatusText = "Creating room...";
            var result = await _controller.CreateRoomAsync(_cdnBaseUri, ct);
            RoomId = result.RoomId;
            HasRoom = true;
            IsOwner = true;
            UpdateSubmitUrl();
            if (_settings is not null)
            {
                _settings.SqRoomId = result.RoomId;
                _settings.SqOwnerToken = result.OwnerToken;
                _settings.SqCdnBaseUrl = _cdnBaseUri.AbsoluteUri;
                SettingsSaveRequested?.Invoke(_settings);
            }
            StatusText = "Room created";
            StartPolling();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StatusText = "Room creation failed";
        }
    }

    // ── Manual add ────────────────────────────────────────────────────────────

    private async Task AddToQueueAsync(CancellationToken ct)
    {
        if (!IsOwner || string.IsNullOrWhiteSpace(AddToQueueUrl)) return;
        try
        {
            LastError = string.Empty;
            var channelId = string.IsNullOrWhiteSpace(AddToQueueChannelId) ? null : AddToQueueChannelId.Trim();
            await _controller.AddTrackAsync(AddToQueueUrl.Trim(), null, null, null, channelId, ct);
            AddToQueueUrl = string.Empty;
            await PollOnceAsync();
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    // ── Settings save ─────────────────────────────────────────────────────────

    private async Task SaveSettingsAsync(CancellationToken ct)
    {
        if (!IsOwner) return;
        try
        {
            LastError = string.Empty;
            var settings = BuildSqSettings();
            // Nothing loaded into the settings panel yet (e.g. saving right after Create
            // Room, before the first poll) — omit rather than send an empty list, which
            // the backend rejects ("at least one queue channel required"). The room
            // already has its default channel server-side; there's nothing to overwrite.
            var channels = Channels.Count > 0 ? BuildQueueChannels() : null;
            var room = await _controller.SaveSettingsAsync(SqEnabled, settings, null, channels, BuildMixPattern(), ct);
            ApplyRoomSnapshot(room);
            StatusText = "Settings saved";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private async Task ToggleAcceptingAsync(CancellationToken ct)
    {
        if (!IsOwner) return;
        try
        {
            LastError = string.Empty;
            var room = await _controller.SetAcceptingSubmissionsAsync(!AcceptingSubmissions, ct);
            ApplyQueueSnapshot(room);
            AcceptingSubmissions = room.AcceptingSubmissions;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private SqSettings BuildSqSettings() => new(
        RequireApproval: RequireApproval,
        AllowDuplicates: AllowDuplicates,
        AllowLinkSubmissions: AllowLinkSubmissions,
        MaxQueueLength: int.TryParse(MaxQueueLength, out var mq) ? mq : 50,
        MaxSubmissionsPerPerson: int.TryParse(MaxPerPerson, out var mp) ? mp : 2,
        SkipBypassesLimit: SkipBypassesLimit,
        QueueEntryFee: new SqFeeSettings(QueueFeeEnabled, ParseAmount(QueueFeeAmount), "USD"),
        Skip: new SqFeeSettings(SkipFeeEnabled, ParseAmount(SkipFeeAmount), "USD"),
        SuperSkip: new SqFeeSettings(SuperSkipFeeEnabled, ParseAmount(SuperSkipFeeAmount), "USD"));

    private static double ParseAmount(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0.0;

    // Priority isn't typed by the user anymore — it's just list position. The top row
    // gets the highest number so it sorts first (and read_sq_room_file's "first listed"
    // rule makes it the default lane too — one ordered list drives both).
    private List<SqQueueChannel> BuildQueueChannels()
    {
        var count = Channels.Count;
        return Channels.Select((c, i) => new SqQueueChannel(
            Id: c.Id,
            Name: string.IsNullOrWhiteSpace(c.Name) ? c.Id : c.Name.Trim(),
            Priority: count - i,
            DiscordChannelId: string.IsNullOrWhiteSpace(c.DiscordChannelId) ? null : c.DiscordChannelId.Trim()
        )).ToList();
    }

    private List<SqMixSlot> BuildMixPattern() => MixPattern
        .Where(s => !string.IsNullOrWhiteSpace(s.ChannelId))
        .Select(s => new SqMixSlot(s.ChannelId.Trim(), int.TryParse(s.Count, out var c) ? Math.Max(1, c) : 1))
        .ToList();

    // ── Queue channel management ─────────────────────────────────────────────

    private void AddChannel()
    {
        // A short, typeable id (not a slug of Name) so renaming a channel later doesn't
        // break its share link or any mix pattern slots that reference it. Guarded
        // against collisions with whatever got loaded from the room (e.g. a fresh
        // session's counter starting back at 1 while "channel1" already exists).
        string id;
        do { id = $"channel{++_channelSeq}"; } while (Channels.Any(c => c.Id == id));
        // New channels start at the bottom (lowest priority) — a fresh lane shouldn't
        // silently outrank ones the streamer already set up.
        Channels.Add(new SqChannelVm(id, $"Channel {Channels.Count + 1}", null, RemoveChannel, CopyChannelLink, MoveChannelUp, MoveChannelDown));
        RefreshChannelShareUrls();
    }

    private void RemoveChannel(SqChannelVm channel)
    {
        // At least one channel must always exist — it's the default lane for links/bot
        // posts that don't specify one.
        if (Channels.Count <= 1) return;
        Channels.Remove(channel);
        foreach (var slot in MixPattern.Where(s => s.ChannelId == channel.Id).ToList())
            MixPattern.Remove(slot);
    }

    private void CopyChannelLink(string url) => CopyToClipboardRequested?.Invoke(url);

    private void MoveChannelUp(SqChannelVm channel)
    {
        var i = Channels.IndexOf(channel);
        if (i > 0) Channels.Move(i, i - 1);
    }

    private void MoveChannelDown(SqChannelVm channel)
    {
        var i = Channels.IndexOf(channel);
        if (i >= 0 && i < Channels.Count - 1) Channels.Move(i, i + 1);
    }

    private void AddMixSlot()
    {
        if (Channels.Count == 0) return;
        MixPattern.Add(new SqMixSlotVm(Channels[0].Id, 1, RemoveMixSlot, MoveMixSlotUp, MoveMixSlotDown));
    }

    private void RemoveMixSlot(SqMixSlotVm slot) => MixPattern.Remove(slot);

    private void MoveMixSlotUp(SqMixSlotVm slot)
    {
        var i = MixPattern.IndexOf(slot);
        if (i > 0) MixPattern.Move(i, i - 1);
    }

    private void MoveMixSlotDown(SqMixSlotVm slot)
    {
        var i = MixPattern.IndexOf(slot);
        if (i >= 0 && i < MixPattern.Count - 1) MixPattern.Move(i, i + 1);
    }

    private void RefreshChannelShareUrls()
    {
        if (string.IsNullOrWhiteSpace(RoomId)) return;
        var baseUrl = _cdnBaseUri.AbsoluteUri.TrimEnd('/');
        foreach (var channel in Channels)
            channel.ShareUrl = $"{baseUrl}/spectralis/web-share/sq.html?room={Uri.EscapeDataString(RoomId)}&ch={Uri.EscapeDataString(channel.Id)}";
    }

    // ── Queue actions ─────────────────────────────────────────────────────────

    internal async Task ApproveAsync(string id)
    {
        try { await _controller.ApproveAsync(id, CancellationToken.None); await PollOnceAsync(); }
        catch (Exception ex) { LastError = ex.Message; }
    }

    internal async Task RejectAsync(string id)
    {
        try { await _controller.RejectAsync(id, CancellationToken.None); await PollOnceAsync(); }
        catch (Exception ex) { LastError = ex.Message; }
    }

    internal async Task DeleteAsync(string id)
    {
        try { await _controller.DeleteAsync(id, CancellationToken.None); await PollOnceAsync(); }
        catch (Exception ex) { LastError = ex.Message; }
    }

    internal async Task SetStatusAsync(string id, SqStatus status)
    {
        try
        {
            var wire = status switch
            {
                SqStatus.Played => "played",
                SqStatus.Skipped => "skipped",
                _ => "queued"
            };
            await _controller.SetStatusAsync(id, wire, CancellationToken.None);
            await PollOnceAsync();
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    internal async Task MarkNowPlayingAsync(string? id)
    {
        try { await _controller.SetNowPlayingAsync(id, CancellationToken.None); await PollOnceAsync(); }
        catch (Exception ex) { LastError = ex.Message; }
    }

    internal async Task PlayItemAsync(SqItemVm item)
    {
        try
        {
            var playbackUrl = ResolvePlaybackUrl(item);
            if (playbackUrl is not null && PlayTrackRequested is not null)
                await PlayTrackRequested(playbackUrl);

            await MarkNowPlayingAsync(item.Id);
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    private string? ResolvePlaybackUrl(SqItemVm item)
    {
        if (!string.IsNullOrWhiteSpace(item.Url))
            return item.Url;

        if (!string.IsNullOrWhiteSpace(item.FileId) && _settings is not null && !string.IsNullOrWhiteSpace(_settings.SqOwnerToken))
        {
            var baseUrl = _cdnBaseUri.AbsoluteUri.TrimEnd('/');
            // Append the original extension so the player's URL-sniffing recognizes this as
            // direct audio; the backend strips it back off before looking up the file (see get_sq_upload).
            var extension = string.IsNullOrWhiteSpace(item.FileName) ? ".mp3" : Path.GetExtension(item.FileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".mp3";
            return $"{baseUrl}/streamer-queue/v1/rooms/{Uri.EscapeDataString(RoomId)}/uploads/{Uri.EscapeDataString(item.FileId)}{extension}?ownerToken={Uri.EscapeDataString(_settings.SqOwnerToken)}";
        }

        return null;
    }

    public async Task ReorderAsync(IEnumerable<string> orderedIds)
    {
        try { await _controller.ReorderAsync(orderedIds, CancellationToken.None); }
        catch (Exception ex) { LastError = ex.Message; }
    }

    // ── Stripe ────────────────────────────────────────────────────────────────

    private async Task ConnectStripeAsync(CancellationToken ct)
    {
        try
        {
            var result = await _controller.GetStripeConnectUrlAsync(ct);
            OpenUrlRequested?.Invoke(result.ConnectUrl);
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    private async Task DisconnectStripeAsync(CancellationToken ct)
    {
        try
        {
            await _controller.StripeDisconnectAsync(ct);
            StripeConnected = false;
            StripeStatus = "Not connected";
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    // ── Discord pairing ──────────────────────────────────────────────────────────

    private async Task LinkDiscordAsync(CancellationToken ct)
    {
        try
        {
            LastError = string.Empty;
            var result = await _controller.CreateDiscordPinAsync(ct);
            DiscordPinDisplay = result.Pin;
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    // ── Clipboard ─────────────────────────────────────────────────────────────

    private void CopySubmitUrl() => CopyToClipboardRequested?.Invoke(SubmitUrl);

    // ── Polling ───────────────────────────────────────────────────────────────

    private void StartPolling()
    {
        _pollCts.Cancel();
        _pollCts = new CancellationTokenSource();
        _ = PollLoopAsync(_pollCts.Token);
    }

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxPollBackoff = TimeSpan.FromMinutes(5);

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await PollOnceAsync();

            // A room that's been failing for a while (dead endpoint, network outage) doesn't
            // need hammering every 10s forever — widen the wait with the failure streak.
            var backoff = PollInterval * (1 + _controller.ConsecutiveFailureCount);
            var delay = backoff < MaxPollBackoff ? backoff : MaxPollBackoff;
            try { await Task.Delay(delay, ct); } catch { break; }
        }
    }

    private async Task PollOnceAsync()
    {
        var room = await _controller.PollAsync(CancellationToken.None);
        if (room is not null)
            ApplyQueueSnapshot(room);
        else if (_controller.LastError is not null)
            LastError = _controller.LastError;
    }

    // ── Snapshot application ──────────────────────────────────────────────────

    // Full snapshot: settings + queue. Only safe to call right after a load or
    // an explicit save — the periodic poll must not clobber in-progress edits
    // to the settings form (e.g. "Queue enabled" getting flipped back off
    // before the user has a chance to click Save).
    private void ApplyRoomSnapshot(SqRoom room)
    {
        SqEnabled = room.Enabled;

        if (room.Settings is { } s)
        {
            RequireApproval = s.RequireApproval;
            AllowDuplicates = s.AllowDuplicates;
            AllowLinkSubmissions = s.AllowLinkSubmissions;
            MaxQueueLength = s.MaxQueueLength.ToString();
            MaxPerPerson = s.MaxSubmissionsPerPerson.ToString();
            SkipBypassesLimit = s.SkipBypassesLimit;
            QueueFeeEnabled = s.QueueEntryFee.Enabled;
            QueueFeeAmount = s.QueueEntryFee.Amount.ToString("F2");
            SkipFeeEnabled = s.Skip.Enabled;
            SkipFeeAmount = s.Skip.Amount.ToString("F2");
            SuperSkipFeeEnabled = s.SuperSkip.Enabled;
            SuperSkipFeeAmount = s.SuperSkip.Amount.ToString("F2");
        }

        Channels.Clear();
        // Sort by the server's Priority so the list visually matches actual ranking —
        // list order IS priority here, top to bottom (see SqChannelVm).
        foreach (var c in (room.QueueChannels ?? []).OrderByDescending(c => c.Priority))
            Channels.Add(new SqChannelVm(c.Id, c.Name, c.DiscordChannelId, RemoveChannel, CopyChannelLink, MoveChannelUp, MoveChannelDown));
        RefreshChannelShareUrls();

        MixPattern.Clear();
        foreach (var slot in room.ChannelMixPattern ?? [])
            MixPattern.Add(new SqMixSlotVm(slot.ChannelId, slot.Count, RemoveMixSlot, MoveMixSlotUp, MoveMixSlotDown));

        ApplyQueueSnapshot(room);
    }

    // Live queue state only: submissions, now-playing, wait estimates. Safe to
    // call on every poll tick without disturbing an unsaved settings edit.
    private void ApplyQueueSnapshot(SqRoom room)
    {
        AcceptingSubmissions = room.AcceptingSubmissions;

        _channelNames.Clear();
        foreach (var c in room.QueueChannels ?? [])
            _channelNames[c.Id] = c.Name;

        var nowTier = room.NowPlayingTier;
        IsP2wActive = nowTier is "skip" or "super_skip";

        var nowId = room.NowPlayingId;

        // Rebuild queue items from ordered queue
        var ordered = room.OrderedQueue ?? room.Submissions ?? [];
        QueueItems.Clear();
        PendingItems.Clear();
        HistoryItems.Clear();
        NowPlayingItem = null;

        foreach (var sub in ordered.Where(s => s.Status is SqStatus.Queued or SqStatus.Approved or SqStatus.Playing))
        {
            var item = MakeSqItemVm(sub);
            if (sub.Id == nowId)
                NowPlayingItem = item;
            else
                QueueItems.Add(item);
        }

        foreach (var sub in (room.Submissions ?? []).Where(s => s.Status == SqStatus.Pending))
            PendingItems.Add(MakeSqItemVm(sub));

        // Most-recently-touched played/skipped items first, so "not played yet" has
        // something obvious to grab right after a misclick.
        foreach (var sub in (room.Submissions ?? [])
                     .Where(s => s.Status is SqStatus.Played or SqStatus.Skipped)
                     .Reverse()
                     .Take(20))
            HistoryItems.Add(MakeSqItemVm(sub));

        // Drop scraped-metadata entries for submissions that left the queue (played,
        // rejected, deleted) so this doesn't grow for the life of the app session.
        var liveIds = (room.Submissions ?? []).Select(s => s.Id).ToHashSet();
        foreach (var staleId in _scrapedMetadata.Keys.Where(id => !liveIds.Contains(id)).ToList())
            _scrapedMetadata.Remove(staleId);

        this.RaisePropertyChanged(nameof(NowPlayingItem));
        StatusText = !SqEnabled ? "Queue disabled"
            : !AcceptingSubmissions ? $"{QueueItems.Count} in queue (closed to new requests)"
            : $"{QueueItems.Count} in queue";
    }

    private SqItemVm MakeSqItemVm(SqSubmission sub)
    {
        // Badge is only useful once there's more than one lane to distinguish.
        var channelName = _channelNames.Count > 1 && sub.QueueChannelId is { } cid && _channelNames.TryGetValue(cid, out var name)
            ? name : null;

        var item = new SqItemVm(sub, channelName,
            id => _ = ApproveAsync(id),
            id => _ = RejectAsync(id),
            id => _ = DeleteAsync(id),
            item => _ = PlayItemAsync(item),
            (id, status) => _ = SetStatusAsync(id, status));

        if (_scrapedMetadata.TryGetValue(sub.Id, out var cached))
        {
            if (!string.IsNullOrWhiteSpace(cached.Title)) item.Title = cached.Title!;
            if (!string.IsNullOrWhiteSpace(cached.Artist)) item.Artist = cached.Artist;
        }
        else if (sub.SourceKind == "link" && !string.IsNullOrWhiteSpace(sub.Url) && string.IsNullOrWhiteSpace(sub.Title))
        {
            _ = ScrapeMetadataAsync(sub.Id, sub.Url, item);
        }

        return item;
    }

    /// <summary>Downloads sub.Url through the same resolver actual playback uses (so
    /// YouTube/SoundCloud/etc. get real title/artist, not just a URL-derived guess),
    /// reads embedded tags for plain audio files, then discards the download — this is
    /// only here to name the queue entry, not to pre-cache it for playback.</summary>
    private async Task ScrapeMetadataAsync(string submissionId, string url, SqItemVm item)
    {
        if (!_scrapeInFlight.Add(submissionId)) return;
        await _scrapeGate.WaitAsync();
        string? cachedPath = null;
        try
        {
            var resolved = await _openUrlService.ResolveAsync(url, CancellationToken.None, quickOnly: true);
            var title = resolved.Title;
            var artist = resolved.Artist;

            if (resolved.Kind == RemoteAudioServiceKind.DirectAudio)
            {
                // A plain file link (e.g. a raw .mp3 URL) only gets a filename-derived
                // title from the quick resolve — download it and read the embedded tags
                // for the real thing instead.
                cachedPath = await RemoteAudioCache.DownloadAsync(
                    resolved.AudioUrl, resolved.DownloadExtension, CancellationToken.None, requestInitialRange: true);
                var tags = TrackMetadataReader.Read(cachedPath);
                if (!string.IsNullOrWhiteSpace(tags.Title)) title = tags.Title;
                if (!string.IsNullOrWhiteSpace(tags.Artist)) artist = tags.Artist;
            }

            _scrapedMetadata[submissionId] = (title, artist);
            if (!string.IsNullOrWhiteSpace(title)) item.Title = title!;
            if (!string.IsNullOrWhiteSpace(artist)) item.Artist = artist;
        }
        catch
        {
            _scrapedMetadata[submissionId] = (null, null);
        }
        finally
        {
            RemoteAudioCache.TryDelete(cachedPath);
            _scrapeInFlight.Remove(submissionId);
            _scrapeGate.Release();
        }
    }

    private void UpdateSubmitUrl()
    {
        if (string.IsNullOrWhiteSpace(RoomId)) return;
        var base_ = _cdnBaseUri.AbsoluteUri.TrimEnd('/');
        SubmitUrl = $"{base_}/spectralis/web-share/sq.html?room={Uri.EscapeDataString(RoomId)}";
    }
}
