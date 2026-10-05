using System.Collections.ObjectModel;
using Avalonia.Threading;
using ReactiveUI;
using Spectralis.Core.Diagnostics;

namespace Spectralis.App.ViewModels;

/// <summary>One line in the Network tab. Wraps an immutable entry; a changed entry gets a new row object.</summary>
public sealed class NetworkRowViewModel
{
    public NetworkRowViewModel(NetworkEntry entry) => Entry = entry;

    public NetworkEntry Entry { get; }
    public long Id => Entry.Id;
    public string Time => Entry.StartedAt.ToString("HH:mm:ss.fff");
    public string Method => Entry.Method;
    public string Status => NetworkLogFormatter.StatusText(Entry);
    public string Host => Entry.Host;
    public string Path => NetworkLogFormatter.PathAndQuery(Entry);
    public string Source => Entry.Source;
    public string Duration => NetworkLogFormatter.Duration(Entry.DurationMs);
    public string Size => NetworkLogFormatter.SizeText(Entry);
    public bool IsError => Entry.IsError;
    public bool IsLive => Entry.State is NetworkState.Live or NetworkState.Pending;
}

/// <summary>Backs the Network tab: the filtered list, the selected call's details and the pause/clear controls.</summary>
public sealed class NetworkLogViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(300);

    private readonly NetworkLogStore _store;
    private readonly DispatcherTimer _timer;
    private int _dirty = 1;
    private bool _rebuilding;
    private string _filterText = string.Empty;
    private bool _errorsOnly;
    private string _sourceFilter = AllSources;
    private bool _paused;
    private NetworkRowViewModel? _selected;
    private long _selectedId;
    private string _summary = string.Empty;
    private string _detail = string.Empty;

    public const string AllSources = "All sources";

    public NetworkLogViewModel() : this(NetworkLog.Default) { }

    public NetworkLogViewModel(NetworkLogStore store)
    {
        _store = store;
        _paused = !store.Enabled;
        Sources.Add(AllSources);
        // The store changes from any thread and many times a second during a download or a busy socket, so the
        // list is rebuilt at most once per interval, and only when something changed.
        _timer = new DispatcherTimer { Interval = RefreshInterval };
        _timer.Tick += (_, _) => RefreshIfDirty();
    }

    /// <summary>Starts following the log. The tab calls this whenever it becomes visible.</summary>
    public void Activate()
    {
        _store.Changed -= OnStoreChanged;
        _store.Changed += OnStoreChanged;
        _timer.Start();
        Refresh();
    }

    /// <summary>Stops following the log while the tab is hidden. A paused log stays paused.</summary>
    public void Deactivate()
    {
        _store.Changed -= OnStoreChanged;
        _timer.Stop();
    }

    public ObservableCollection<NetworkRowViewModel> Rows { get; } = [];
    public ObservableCollection<string> Sources { get; } = [];

    public string FilterText
    {
        get => _filterText;
        set { this.RaiseAndSetIfChanged(ref _filterText, value); Refresh(); }
    }

    public bool ErrorsOnly
    {
        get => _errorsOnly;
        set { this.RaiseAndSetIfChanged(ref _errorsOnly, value); Refresh(); }
    }

    public string SourceFilter
    {
        get => _sourceFilter;
        set { this.RaiseAndSetIfChanged(ref _sourceFilter, value ?? AllSources); Refresh(); }
    }

    /// <summary>While paused nothing new is recorded; what is already listed stays.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            this.RaiseAndSetIfChanged(ref _paused, value);
            _store.Enabled = !value;
            this.RaisePropertyChanged(nameof(PauseLabel));
        }
    }

    public string PauseLabel => _paused ? "Resume" : "Pause";

    public NetworkRowViewModel? Selected
    {
        get => _selected;
        set
        {
            this.RaiseAndSetIfChanged(ref _selected, value);
            // The grid reports a null selection while the rows are being rebuilt; that is not the user deselecting.
            if (_rebuilding) return;
            _selectedId = value?.Id ?? 0;
            Detail = value is null ? string.Empty : NetworkLogFormatter.Detail(value.Entry);
        }
    }

    public string Summary
    {
        get => _summary;
        private set => this.RaiseAndSetIfChanged(ref _summary, value);
    }

    public string Detail
    {
        get => _detail;
        private set => this.RaiseAndSetIfChanged(ref _detail, value);
    }

    public void Clear()
    {
        Selected = null;
        _store.Clear();
    }

    /// <summary>Every call that passes the current filter, as text for a bug report.</summary>
    public string BuildReport() => NetworkLogFormatter.Report(Rows.Select(r => r.Entry).OrderBy(e => e.Id));

    public void Dispose()
    {
        Deactivate();
        // Leaving the window must not leave the log switched off.
        if (_paused) _store.Enabled = true;
    }

    private void OnStoreChanged() => Interlocked.Exchange(ref _dirty, 1);

    private void RefreshIfDirty()
    {
        if (Interlocked.Exchange(ref _dirty, 0) == 1) Refresh();
    }

    private void Refresh()
    {
        Interlocked.Exchange(ref _dirty, 0);
        var all = _store.Snapshot();

        var sources = all.Select(e => e.Source).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        if (!Sources.Skip(1).SequenceEqual(sources))
        {
            while (Sources.Count > 1) Sources.RemoveAt(1);
            foreach (var source in sources) Sources.Add(source);
        }

        // Newest first, so the call you just made is at the top.
        var shown = all
            .Where(e => NetworkLogFormatter.Matches(e, _filterText, _errorsOnly, _sourceFilter == AllSources ? null : _sourceFilter))
            .OrderByDescending(e => e.Id)
            .ToList();

        _rebuilding = true;
        NetworkRowViewModel? keep;
        try
        {
            Rows.Clear();
            foreach (var entry in shown) Rows.Add(new NetworkRowViewModel(entry));

            // Rows are rebuilt, so put the selection back on the same call (and refresh its details as it finishes).
            keep = _selectedId == 0 ? null : Rows.FirstOrDefault(r => r.Id == _selectedId);
            _selected = keep;
            this.RaisePropertyChanged(nameof(Selected));
        }
        finally { _rebuilding = false; }
        if (keep is null) _selectedId = 0;
        Detail = keep is null ? string.Empty : NetworkLogFormatter.Detail(keep.Entry);

        var errors = all.Count(e => e.IsError);
        var live = all.Count(e => e.State is NetworkState.Pending or NetworkState.Live);
        Summary = $"{shown.Count} of {all.Count} calls · {errors} failed · {live} open" + (_paused ? " · paused" : string.Empty);
    }
}
