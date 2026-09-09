using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using Spectralis.App.Services;
using Spectralis.Core.Common;
using Spectralis.Core.Integrations.Spotify;
using Spectralis.Core.Metadata;

namespace Spectralis.App.ViewModels;

/// <summary>The three randomizer tabs. String constants (not an enum) so they round-trip
/// straight through <see cref="SavedWheel.Mode"/> and the mode ComboBox binding.</summary>
public static class RandomizerMode
{
    public const string Text = "text";
    public const string Songs = "songs";
    public const string Playlists = "playlists";

    public static readonly IReadOnlyList<string> All = [Text, Songs, Playlists];

    public static string Label(string mode) => mode switch
    {
        Songs => "Songs",
        Playlists => "Playlists",
        _ => "Text",
    };
}

/// <summary>A selectable entry in the mode ComboBox — key drives logic, label is shown.</summary>
public sealed record RandomizerModeOption(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One row in the "add songs / playlists" picker. Not persisted — it only exists to
/// be turned into a <see cref="WheelEntry"/> when the user adds it.</summary>
public sealed class MusicPickCandidate(WheelEntryKind kind, string reference, string title, string subtitle)
{
    public WheelEntryKind Kind { get; } = kind;
    public string Ref { get; } = reference;
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    public WheelEntry ToEntry() => new(Title, Kind, Ref, Subtitle);
}

public sealed class RandomizerToolsViewModel : ViewModelBase
{
    private readonly LibraryDatabase? _library;
    private readonly PlaylistsViewModel? _playlists;
    private readonly AppSettings? _settings;
    private readonly SpotifyService _spotify = new();
    private readonly Func<IReadOnlyList<MusicPickCandidate>>? _currentQueueProvider;
    private readonly Func<IReadOnlyList<string>, int, Task>? _playSongs;
    private readonly Action<IReadOnlyDictionary<string, (string Title, string Artist)>>? _setQueueMetadata;

    private string _coinResult = string.Empty;
    private bool _isFlipping;
    private bool _isSpinning;
    private string _wheelResult = string.Empty;
    private WheelEntry? _wheelResultEntry;
    private string _notepadText = string.Empty;
    private string _newWheelName = string.Empty;
    private string _selectedMode = RandomizerMode.Text;
    private RandomizerModeOption _selectedModeOption;
    private string _musicSearchText = string.Empty;
    private bool _loadingWheel;
    private List<TrackRow> _libraryRows = [];
    private List<MusicPickCandidate> _spotifyCandidates = [];
    private CancellationTokenSource? _spotifySearchCts;

    public RandomizerToolsViewModel(
        LibraryDatabase? library = null,
        PlaylistsViewModel? playlists = null,
        AppSettings? settings = null,
        Func<IReadOnlyList<MusicPickCandidate>>? currentQueueProvider = null,
        Func<IReadOnlyList<string>, int, Task>? playSongs = null,
        Action<IReadOnlyDictionary<string, (string Title, string Artist)>>? setQueueMetadata = null)
    {
        _library = library;
        _playlists = playlists;
        _settings = settings;
        _currentQueueProvider = currentQueueProvider;
        _playSongs = playSongs;
        _setQueueMetadata = setQueueMetadata;
        _selectedModeOption = Modes[0];

        WheelEntries = [];
        foreach (var text in new[] { "Option 1", "Option 2", "Option 3" })
            WheelEntries.Add(new WheelEntry(text));
        _notepadText = string.Join('\n', WheelEntries.Select(e => e.Text));

        FlipCoinCommand = ReactiveCommand.Create(RequestFlip,
            this.WhenAnyValue(x => x.IsFlipping, f => !f));

        SpinCommand = ReactiveCommand.Create(RequestSpin,
            this.WhenAnyValue(x => x.IsSpinning, s => !s));

        SaveWheelCommand = ReactiveCommand.Create(SaveWheel,
            this.WhenAnyValue(x => x.NewWheelName, x => !string.IsNullOrWhiteSpace(x)));

        PlayResultCommand = ReactiveCommand.CreateFromTask(PlayResultAsync,
            this.WhenAnyValue(x => x.WheelResultEntry, e => e is { IsMusic: true }));

        AddCurrentQueueCommand = ReactiveCommand.Create(AddCurrentQueue,
            Observable.Return(_currentQueueProvider is not null));

        this.WhenAnyValue(x => x.MusicSearchText)
            .Throttle(TimeSpan.FromMilliseconds(200))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => ApplyMusicFilter());

        this.WhenAnyValue(x => x.MusicSearchText)
            .Throttle(TimeSpan.FromMilliseconds(400))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(async _ => await RunSpotifySearchAsync());

        RefreshSavedWheels();
        LoadLibraryRows();
        RefreshPlaylistCandidates();
    }

    public ObservableCollection<WheelEntry> WheelEntries { get; }
    public ObservableCollection<SavedWheel> SavedWheels { get; } = [];

    /// <summary>Library + Spotify search results for the "add a song" picker (Songs tab).</summary>
    public ObservableCollection<MusicPickCandidate> MusicResults { get; } = [];

    /// <summary>Every saved playlist, for the "add a playlist" picker (Playlists tab) and the
    /// "load a playlist's songs" action (Songs tab).</summary>
    public ObservableCollection<MusicPickCandidate> PlaylistCandidates { get; } = [];

    public IReadOnlyList<RandomizerModeOption> Modes { get; } =
        RandomizerMode.All.Select(m => new RandomizerModeOption(m, RandomizerMode.Label(m))).ToList();

    /// <summary>Two-way bound to the mode ComboBox. Delegates to <see cref="SelectedMode"/>, which
    /// stays the source of truth for logic and persistence.</summary>
    public RandomizerModeOption SelectedModeOption
    {
        get => _selectedModeOption;
        set
        {
            if (value is null) return;
            this.RaiseAndSetIfChanged(ref _selectedModeOption, value);
            SelectedMode = value.Key;
        }
    }

    public string SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (!RandomizerMode.All.Contains(value)) value = RandomizerMode.Text;
            var changed = _selectedMode != value;
            this.RaiseAndSetIfChanged(ref _selectedMode, value);

            var option = Modes.FirstOrDefault(m => m.Key == value) ?? Modes[0];
            this.RaiseAndSetIfChanged(ref _selectedModeOption, option, nameof(SelectedModeOption));

            // Each tab owns its own kind of slice — dropping the mismatched ones on a switch keeps
            // the wheel from carrying, say, the placeholder text options into a song wheel. Skipped
            // during LoadWheel (which sets the mode itself before repopulating).
            if (changed && !_loadingWheel)
            {
                DropEntriesNotMatching(value);
                WheelResult = string.Empty;
                WheelResultEntry = null;
            }

            this.RaisePropertyChanged(nameof(IsTextMode));
            this.RaisePropertyChanged(nameof(IsSongMode));
            this.RaisePropertyChanged(nameof(IsPlaylistMode));
            this.RaisePropertyChanged(nameof(IsMusicMode));
            this.RaisePropertyChanged(nameof(EntriesHeader));
            this.RaisePropertyChanged(nameof(SpinButtonText));

            if (IsSongMode) ApplyMusicFilter();
            else if (IsPlaylistMode) RefreshPlaylistCandidates();
        }
    }

    public bool IsTextMode => _selectedMode == RandomizerMode.Text;
    public bool IsSongMode => _selectedMode == RandomizerMode.Songs;
    public bool IsPlaylistMode => _selectedMode == RandomizerMode.Playlists;
    public bool IsMusicMode => IsSongMode || IsPlaylistMode;

    public string EntriesHeader => _selectedMode switch
    {
        RandomizerMode.Songs => "SONGS ON THE WHEEL",
        RandomizerMode.Playlists => "PLAYLISTS ON THE WHEEL",
        _ => "WHEEL ENTRIES",
    };

    public string SpinButtonText => _selectedMode switch
    {
        RandomizerMode.Songs => "Pick a song",
        RandomizerMode.Playlists => "Pick a playlist",
        _ => "Spin!",
    };

    public string MusicSearchText
    {
        get => _musicSearchText;
        set => this.RaiseAndSetIfChanged(ref _musicSearchText, value);
    }

    /// <summary>Raw multiline entry editor. One wheel entry per non-blank line. Text tab only.</summary>
    public string NotepadText
    {
        get => _notepadText;
        set
        {
            this.RaiseAndSetIfChanged(ref _notepadText, value);
            SyncEntriesFromNotepad();
        }
    }

    public string NewWheelName
    {
        get => _newWheelName;
        set => this.RaiseAndSetIfChanged(ref _newWheelName, value);
    }

    public string CoinResult
    {
        get => _coinResult;
        set => this.RaiseAndSetIfChanged(ref _coinResult, value);
    }

    public bool IsFlipping
    {
        get => _isFlipping;
        set => this.RaiseAndSetIfChanged(ref _isFlipping, value);
    }

    public bool IsSpinning
    {
        get => _isSpinning;
        set => this.RaiseAndSetIfChanged(ref _isSpinning, value);
    }

    public string WheelResult
    {
        get => _wheelResult;
        set
        {
            this.RaiseAndSetIfChanged(ref _wheelResult, value);
            this.RaisePropertyChanged(nameof(HasWheelResult));
        }
    }

    /// <summary>The slice the last spin landed on. Drives the "Play" button (music tabs).</summary>
    public WheelEntry? WheelResultEntry
    {
        get => _wheelResultEntry;
        private set
        {
            this.RaiseAndSetIfChanged(ref _wheelResultEntry, value);
            this.RaisePropertyChanged(nameof(CanPlayResult));
            this.RaisePropertyChanged(nameof(PlayResultText));
        }
    }

    public bool HasWheelResult => !string.IsNullOrEmpty(_wheelResult);

    public bool CanPlayResult => _wheelResultEntry is { IsMusic: true };

    public string PlayResultText => _wheelResultEntry?.Kind == WheelEntryKind.Playlist
        ? "Play playlist"
        : "Play song";

    public ReactiveCommand<Unit, Unit> FlipCoinCommand { get; }
    public ReactiveCommand<Unit, Unit> SpinCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveWheelCommand { get; }
    public ReactiveCommand<Unit, Unit> PlayResultCommand { get; }
    public ReactiveCommand<Unit, Unit> AddCurrentQueueCommand { get; }

    public event Action? SpinRequested;
    public event Action? FlipRequested;

    private void RequestFlip()
    {
        IsFlipping = true;
        FlipRequested?.Invoke();
    }

    public void FinishFlip()
    {
        CoinResult = Random.Shared.Next(2) == 0 ? "Heads" : "Tails";
        IsFlipping = false;
    }

    private void RequestSpin()
    {
        if (WheelEntries.Count == 0) return;
        IsSpinning = true;
        WheelResult = string.Empty;
        WheelResultEntry = null;
        SpinRequested?.Invoke();
    }

    /// <summary>Called by the view once the wheel animation settles on a slice.</summary>
    public void FinishSpin(WheelEntry winner)
    {
        WheelResult = winner.Text;
        WheelResultEntry = winner;
        IsSpinning = false;
    }

    // ── Music pickers ─────────────────────────────────────────────────────────

    /// <summary>Re-reads the library and playlist browser so a wheel built after the user has
    /// added music elsewhere sees the new content. Called when the Randomizer tab is shown.</summary>
    public void RefreshMusicSources()
    {
        LoadLibraryRows();
        RefreshPlaylistCandidates();
    }

    private void LoadLibraryRows()
    {
        if (_library is null) return;
        try
        {
            _libraryRows = _library.GetAllEntries().Select(TrackRow.From).ToList();
        }
        catch
        {
            _libraryRows = [];
        }
        ApplyMusicFilter();
    }

    /// <summary>Rebuilds <see cref="PlaylistCandidates"/> from the playlist browser. Cheap; safe to
    /// call whenever the Randomizer tab is shown.</summary>
    public void RefreshPlaylistCandidates()
    {
        PlaylistCandidates.Clear();
        if (_playlists is null) return;

        foreach (var row in _playlists.PinnedRows.Concat(_playlists.Rows))
        {
            PlaylistCandidates.Add(new MusicPickCandidate(
                WheelEntryKind.Playlist,
                row.Id.ToString(),
                row.Name,
                row.TrackCountText));
        }
    }

    private void ApplyMusicFilter()
    {
        var needle = _musicSearchText.Trim();
        MusicResults.Clear();

        IEnumerable<TrackRow> local = _libraryRows;
        if (needle.Length > 0)
            local = local.Where(r => r.Matches(needle));

        foreach (var row in local.Take(40))
        {
            MusicResults.Add(new MusicPickCandidate(
                WheelEntryKind.Song, row.Path, row.Title,
                string.Join(" · ", new[] { row.Artist, row.Album }.Where(s => !string.IsNullOrWhiteSpace(s)))));
        }

        foreach (var candidate in _spotifyCandidates)
            MusicResults.Add(candidate);
    }

    private async Task RunSpotifySearchAsync()
    {
        _spotifySearchCts?.Cancel();
        var cts = _spotifySearchCts = new CancellationTokenSource();

        var query = _musicSearchText.Trim();
        if (query.Length < 2 || _settings is null || !_spotify.IsLinked)
        {
            _spotifyCandidates = [];
            ApplyMusicFilter();
            return;
        }

        try
        {
            var clientId = SpotifyClientIdProvider.ResolveClientId(_settings.SpotifyCustomClientId);
            var results = await _spotify.SearchTracksAsync(clientId, query, limit: 12);
            if (cts.IsCancellationRequested) return;

            _spotifyCandidates = results.Select(t => new MusicPickCandidate(
                WheelEntryKind.Song, t.Uri, t.Name,
                string.Join(" · ", new[] { t.Artist, "Spotify" }.Where(s => !string.IsNullOrWhiteSpace(s))))).ToList();
        }
        catch
        {
            _spotifyCandidates = [];
        }

        if (!cts.IsCancellationRequested)
            ApplyMusicFilter();
    }

    public void AddCandidate(MusicPickCandidate? candidate)
    {
        if (candidate is null) return;
        if (WheelEntries.Any(e => e.Kind == candidate.Kind &&
                                  string.Equals(e.Ref, candidate.Ref, StringComparison.OrdinalIgnoreCase)))
        {
            return; // already on the wheel
        }

        WheelEntries.Add(candidate.ToEntry());
    }

    public void RemoveEntry(WheelEntry? entry)
    {
        if (entry is not null)
            WheelEntries.Remove(entry);
    }

    public void ClearEntries()
    {
        WheelEntries.Clear();
        if (IsTextMode)
        {
            _notepadText = string.Empty;
            this.RaisePropertyChanged(nameof(NotepadText));
        }
    }

    private void DropEntriesNotMatching(string mode)
    {
        var keep = mode switch
        {
            RandomizerMode.Songs => WheelEntryKind.Song,
            RandomizerMode.Playlists => WheelEntryKind.Playlist,
            _ => WheelEntryKind.Text,
        };

        for (var i = WheelEntries.Count - 1; i >= 0; i--)
        {
            if (WheelEntries[i].Kind != keep)
                WheelEntries.RemoveAt(i);
        }

        _notepadText = keep == WheelEntryKind.Text
            ? string.Join('\n', WheelEntries.Select(e => e.Text))
            : string.Empty;
        this.RaisePropertyChanged(nameof(NotepadText));
    }

    public void AddCurrentQueue()
    {
        if (_currentQueueProvider is null) return;
        foreach (var candidate in _currentQueueProvider())
            AddCandidate(candidate);
    }

    /// <summary>Songs tab: expands one saved playlist into individual song slices.</summary>
    public void LoadPlaylistSongs(MusicPickCandidate? playlist)
    {
        if (playlist is null || _playlists is null) return;
        if (!Guid.TryParse(playlist.Ref, out var id)) return;

        var row = _playlists.FindRow(id);
        if (row is null) return;

        foreach (var (reference, title, subtitle) in _playlists.GetTrackEntriesForRow(row))
        {
            AddCandidate(new MusicPickCandidate(WheelEntryKind.Song, reference, title, subtitle));
        }
    }

    public async Task PlayResultAsync()
    {
        var entry = _wheelResultEntry;
        if (entry is null || string.IsNullOrWhiteSpace(entry.Ref)) return;

        if (entry.Kind == WheelEntryKind.Playlist)
        {
            if (_playlists is not null && Guid.TryParse(entry.Ref, out var id) &&
                _playlists.FindRow(id) is { } row)
            {
                await _playlists.PlayRowAsync(row);
            }
            return;
        }

        if (entry.Kind == WheelEntryKind.Song && _playSongs is not null)
        {
            if (entry.HasSubtitle)
            {
                var artist = entry.Subtitle!.Split(" · ", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                _setQueueMetadata?.Invoke(new Dictionary<string, (string, string)>
                {
                    [entry.Ref!] = (entry.Text, artist),
                });
            }
            await _playSongs([entry.Ref!], 0);
        }
    }

    // ── Text notepad ─────────────────────────────────────────────────────────

    /// <summary>
    /// Reparses <see cref="NotepadText"/> into <see cref="WheelEntries"/>, one entry per non-blank
    /// line. Entries whose text is unchanged keep their existing color/font/weight settings
    /// (matched by text, not position, so reordering or editing other lines doesn't reset them).
    /// </summary>
    private void SyncEntriesFromNotepad()
    {
        if (!IsTextMode) return; // music tabs manage WheelEntries directly

        var lines = (_notepadText ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var pool = new Dictionary<string, Queue<WheelEntry>>(StringComparer.Ordinal);
        foreach (var entry in WheelEntries)
        {
            if (!pool.TryGetValue(entry.Text, out var queue))
                pool[entry.Text] = queue = new Queue<WheelEntry>();
            queue.Enqueue(entry);
        }

        var next = new List<WheelEntry>(lines.Count);
        foreach (var line in lines)
        {
            if (pool.TryGetValue(line, out var queue) && queue.Count > 0)
                next.Add(queue.Dequeue());
            else
                next.Add(new WheelEntry(line));
        }

        if (next.SequenceEqual(WheelEntries)) return;

        WheelEntries.Clear();
        foreach (var entry in next)
            WheelEntries.Add(entry);
    }

    // ── Saved wheels ─────────────────────────────────────────────────────────

    private void RefreshSavedWheels()
    {
        SavedWheels.Clear();
        foreach (var wheel in SavedWheelStore.LoadAll())
            SavedWheels.Add(wheel);
    }

    public void SaveWheel()
    {
        var name = NewWheelName.Trim();
        if (string.IsNullOrEmpty(name) || WheelEntries.Count == 0) return;

        SavedWheelStore.Save(name, SelectedMode, WheelEntries);
        NewWheelName = string.Empty;
        RefreshSavedWheels();
    }

    public void LoadWheel(SavedWheel wheel)
    {
        if (wheel is null) return;

        _loadingWheel = true;
        try
        {
            SelectedMode = wheel.Mode;

            WheelEntries.Clear();
            foreach (var entry in wheel.Entries)
                WheelEntries.Add(entry.Clone());
        }
        finally
        {
            _loadingWheel = false;
        }

        // Set the backing field directly (not the property) so the notepad text reflects the
        // loaded entries without re-parsing and discarding their color/font/weight settings.
        _notepadText = IsTextMode ? string.Join('\n', WheelEntries.Select(e => e.Text)) : string.Empty;
        this.RaisePropertyChanged(nameof(NotepadText));

        WheelResult = string.Empty;
        WheelResultEntry = null;
    }

    public void DeleteWheel(SavedWheel wheel)
    {
        if (wheel is null) return;
        SavedWheelStore.Delete(wheel.Name);
        RefreshSavedWheels();
    }
}
