using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using Spectralis.App.Services;
using Spectralis.Core.StreamerQueue;

namespace Spectralis.App.ViewModels;

// ── Queue item VM used for the draggable list ─────────────────────────────────

public sealed class SqItemVm : ViewModelBase
{
    private bool _isPending;

    public SqItemVm(SqSubmission sub,
        Action<string> onApprove,
        Action<string> onReject,
        Action<string> onDelete,
        Action<string> onMarkPlaying)
    {
        Id = sub.Id;
        DisplayName = sub.DisplayName;
        Title = sub.Title ?? "(untitled)";
        Artist = sub.Artist;
        Tier = sub.Tier;
        Status = sub.Status;
        DurationSeconds = sub.DurationSeconds;
        SourceKind = sub.SourceKind;
        Url = sub.Url;
        _isPending = sub.Status == SqStatus.Pending;

        ApproveCommand  = ReactiveCommand.Create(() => onApprove(Id), this.WhenAnyValue(x => x.IsPending));
        RejectCommand   = ReactiveCommand.Create(() => onReject(Id));
        DeleteCommand   = ReactiveCommand.Create(() => onDelete(Id));
        PlayCommand     = ReactiveCommand.Create(() => onMarkPlaying(Id));
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Title { get; }
    public string? Artist { get; }
    public SqTier Tier { get; }
    public SqStatus Status { get; }
    public double? DurationSeconds { get; }
    public string SourceKind { get; }
    public string? Url { get; }

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

    public ReactiveCommand<Unit, Unit> ApproveCommand { get; }
    public ReactiveCommand<Unit, Unit> RejectCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> PlayCommand { get; }
}

// ── Main ViewModel ────────────────────────────────────────────────────────────

public sealed class StreamerQueueViewModel : ViewModelBase, IDisposable
{
    private readonly StreamerQueueRoomController _controller = new();
    private CancellationTokenSource _pollCts = new();

    // ── State ─────────────────────────────────────────────────────────────────
    private bool _hasRoom;
    private bool _isOwner;
    private string _roomId = string.Empty;
    private string _submitUrl = string.Empty;
    private string _statusText = "No queue configured";
    private string _lastError = string.Empty;
    private bool _isP2wActive;

    // ── Settings ──────────────────────────────────────────────────────────────
    private bool _sqEnabled;
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

    public event Action<string>? CopyToClipboardRequested;
    public event Action<string>? OpenUrlRequested;
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

    public ObservableCollection<SqItemVm> QueueItems { get; } = [];
    public ObservableCollection<SqItemVm> PendingItems { get; } = [];
    public SqItemVm? NowPlayingItem { get; private set; }

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

    // ── Settings save ─────────────────────────────────────────────────────────

    private async Task SaveSettingsAsync(CancellationToken ct)
    {
        if (!IsOwner) return;
        try
        {
            LastError = string.Empty;
            var settings = BuildSqSettings();
            var room = await _controller.SaveSettingsAsync(SqEnabled, settings, null, ct);
            ApplyRoomSnapshot(room);
            StatusText = "Settings saved";
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

    internal async Task MarkNowPlayingAsync(string? id)
    {
        try { await _controller.SetNowPlayingAsync(id, CancellationToken.None); await PollOnceAsync(); }
        catch (Exception ex) { LastError = ex.Message; }
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

    // ── Clipboard ─────────────────────────────────────────────────────────────

    private void CopySubmitUrl() => CopyToClipboardRequested?.Invoke(SubmitUrl);

    // ── Polling ───────────────────────────────────────────────────────────────

    private void StartPolling()
    {
        _pollCts.Cancel();
        _pollCts = new CancellationTokenSource();
        _ = PollLoopAsync(_pollCts.Token);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await PollOnceAsync();
            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { break; }
        }
    }

    private async Task PollOnceAsync()
    {
        var room = await _controller.PollAsync(CancellationToken.None);
        if (room is not null)
            ApplyRoomSnapshot(room);
        else if (_controller.LastError is not null)
            LastError = _controller.LastError;
    }

    // ── Snapshot application ──────────────────────────────────────────────────

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

        var nowTier = room.NowPlayingTier;
        IsP2wActive = nowTier is "skip" or "super_skip";

        var nowId = room.NowPlayingId;

        // Rebuild queue items from ordered queue
        var ordered = room.OrderedQueue ?? room.Submissions ?? [];
        QueueItems.Clear();
        PendingItems.Clear();
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

        this.RaisePropertyChanged(nameof(NowPlayingItem));
        StatusText = SqEnabled ? $"{QueueItems.Count} in queue" : "Queue disabled";
    }

    private SqItemVm MakeSqItemVm(SqSubmission sub) => new(sub,
        id => _ = ApproveAsync(id),
        id => _ = RejectAsync(id),
        id => _ = DeleteAsync(id),
        id => _ = MarkNowPlayingAsync(id));

    private void UpdateSubmitUrl()
    {
        if (string.IsNullOrWhiteSpace(RoomId)) return;
        var base_ = _cdnBaseUri.AbsoluteUri.TrimEnd('/');
        SubmitUrl = $"{base_}/sq?room={Uri.EscapeDataString(RoomId)}";
    }
}
