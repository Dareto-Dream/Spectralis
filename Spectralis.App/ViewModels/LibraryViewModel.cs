using System.Collections.ObjectModel;
using System.Reactive.Linq;
using ReactiveUI;
using Spectralis.App.Services;
using Spectralis.Core.Analysis;
using Spectralis.Core.Common;
using Spectralis.Core.Metadata;

namespace Spectralis.App.ViewModels;

/// <summary>One library row; raw values for sorting, formatted strings for display.</summary>
public sealed class TrackRow
{
    public required string Path { get; init; }
    public uint TrackNumber { get; init; }
    public string TrackNumberText => TrackNumber > 0 ? TrackNumber.ToString() : string.Empty;
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public required string Album { get; init; }
    public string Genre { get; init; } = string.Empty;
    public double DurationSeconds { get; init; }
    public string DurationText => TimeFormat.FormatSeconds(DurationSeconds);
    public uint Year { get; init; }
    public string YearText => Year > 0 ? Year.ToString() : string.Empty;
    public int PlayCount { get; init; }
    public string PlayCountText => PlayCount > 0 ? PlayCount.ToString() : string.Empty;
    public double Bpm { get; init; }
    public string BpmText => Bpm > 0 ? Bpm.ToString("0") : string.Empty;
    public string Key { get; init; } = string.Empty;
    public int BitrateKbps { get; init; }
    public string BitrateText => BitrateKbps > 0 ? BitrateKbps.ToString() : string.Empty;
    public required string Format { get; init; }
    public long SizeBytes { get; init; }
    public string SizeText => SizeBytes > 0 ? FormatLabel.FormatBytes(SizeBytes) : string.Empty;

    public static TrackRow From(LibraryEntry entry)
    {
        var track = entry.Track;
        return new TrackRow
        {
            Path = track.SourcePath,
            TrackNumber = track.TrackNumber,
            Title = track.DisplayTitle,
            Artist = track.Artist,
            Album = track.Album,
            Genre = track.Genre,
            DurationSeconds = track.Duration.TotalSeconds,
            Year = track.Year,
            PlayCount = entry.PlayCount,
            Bpm = track.Bpm ?? 0,
            Key = track.MusicalKey ?? string.Empty,
            BitrateKbps = track.BitrateKbps,
            Format = track.FormatName,
            SizeBytes = track.FileSizeBytes,
        };
    }

    public bool Matches(string needle) =>
        Title.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
        Artist.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
        Album.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

public sealed class LibraryViewModel : ViewModelBase, IDisposable
{
    private readonly LibraryDatabase _database;
    private readonly LibraryScanner _scanner;
    private readonly Func<IReadOnlyList<string>, int, Task> _playQueue;
    private readonly IDisposable _searchSubscription;
    private readonly AppSettings? _settings;
    private readonly LibraryWatcher _watcher = new();

    private List<TrackRow> _allRows = new();
    private string _searchText = string.Empty;
    private bool _isScanning;
    private string _scanStatus = string.Empty;
    private TrackRow? _selectedRow;
    private string _selectedFilterMode = "All Tracks";
    private bool _offerLegacyImport;
    private readonly string _legacyDbPath;
    private readonly string _legacyDismissFlagPath;

    public LibraryViewModel(
        LibraryDatabase database,
        LibraryScanner scanner,
        Func<IReadOnlyList<string>, int, Task> playQueue,
        AppSettings? settings = null,
        string? legacyDbPath = null,
        string? legacyDismissFlagPath = null)
    {
        _database = database;
        _scanner = scanner;
        _playQueue = playQueue;
        _settings = settings;
        _watcher.FileAdded += (_, path) => OnWatcherFileAdded(path);
        _watcher.FileRemoved += (_, path) => OnWatcherFileRemoved(path);
        _watcher.FileRenamed += (_, change) => OnWatcherFileRenamed(change.OldPath, change.NewPath);
        _legacyDbPath = legacyDbPath ?? LegacyLibraryImporter.DefaultLegacyDatabasePath;
        _legacyDismissFlagPath = legacyDismissFlagPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spectralis", "legacy-import-dismissed");

        _searchSubscription = this.WhenAnyValue(vm => vm.SearchText)
            .Throttle(TimeSpan.FromMilliseconds(200))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => ApplyFilter());

        ReloadFromDatabase();

        // One-time migration offer: legacy index present, new index empty, not dismissed.
        _offerLegacyImport = !HasTracks &&
            LegacyLibraryImporter.LegacyDatabaseExists(_legacyDbPath) &&
            !File.Exists(_legacyDismissFlagPath);
    }

    /// <summary>True while the one-time WinForms library migration offer is showing.</summary>
    public bool OfferLegacyImport
    {
        get => _offerLegacyImport;
        private set => this.RaiseAndSetIfChanged(ref _offerLegacyImport, value);
    }

    public async Task ImportLegacyLibraryAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        try
        {
            var progress = new Progress<LibraryScanProgress>(p =>
                ScanStatus = p.Completed ? string.Empty : $"Importing {p.Scanned:N0}/{p.Total:N0}...");

            var result = await LegacyLibraryImporter.ImportAsync(_legacyDbPath, _database, progress);

            ScanStatus = result.Skipped > 0
                ? $"Imported {result.Imported:N0} tracks; {result.Skipped:N0} missing files logged"
                : $"Imported {result.Imported:N0} tracks from the legacy library";
            ReloadFromDatabase();
            DismissLegacyImport();
        }
        catch (Exception ex)
        {
            ScanStatus = $"Legacy import failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void DismissLegacyImport()
    {
        OfferLegacyImport = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_legacyDismissFlagPath)!);
            File.WriteAllText(_legacyDismissFlagPath, DateTimeOffset.UtcNow.ToString("u"));
        }
        catch
        {
            // Best-effort; the offer simply reappears next launch.
        }
    }

    public ObservableCollection<TrackRow> Rows { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set => this.RaiseAndSetIfChanged(ref _isScanning, value);
    }

    public string ScanStatus
    {
        get => _scanStatus;
        private set => this.RaiseAndSetIfChanged(ref _scanStatus, value);
    }

    public TrackRow? SelectedRow
    {
        get => _selectedRow;
        set => this.RaiseAndSetIfChanged(ref _selectedRow, value);
    }

    public bool HasTracks => _allRows.Count > 0;

    public string CountText
    {
        get
        {
            var trackLabel = _allRows.Count == 1 ? "1 track" : $"{_allRows.Count:N0} tracks";
            var groupLabel = SelectedFilterMode switch
            {
                "Artists" when _allRows.Count > 0 =>
                    $"  ·  {_allRows.Select(r => r.Artist).Where(a => !string.IsNullOrEmpty(a)).Distinct(StringComparer.OrdinalIgnoreCase).Count()} artists",
                "Albums" when _allRows.Count > 0 =>
                    $"  ·  {_allRows.Select(r => r.Album).Where(a => !string.IsNullOrEmpty(a)).Distinct(StringComparer.OrdinalIgnoreCase).Count()} albums",
                "Genres" when _allRows.Count > 0 =>
                    $"  ·  {_allRows.Select(r => r.Genre).Where(g => !string.IsNullOrEmpty(g)).Distinct(StringComparer.OrdinalIgnoreCase).Count()} genres",
                _ => string.Empty,
            };
            return trackLabel + groupLabel;
        }
    }

    /// <summary>Plays the row in the context of the visible (filtered/sorted) list.</summary>
    public Task PlayRowAsync(TrackRow? row)
    {
        if (row is null)
        {
            return Task.CompletedTask;
        }

        var paths = Rows.Select(static r => r.Path).ToList();
        var index = Rows.IndexOf(row);
        return _playQueue(paths, Math.Max(0, index));
    }

    /// <summary>Scans paths into the library without interrupting playback (worker thread).</summary>
    public async Task ScanPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        try
        {
            var progress = new Progress<LibraryScanProgress>(p =>
                ScanStatus = p.Completed
                    ? string.Empty
                    : $"Scanning {p.Scanned:N0}/{p.Total:N0}...");

            var result = await _scanner.ScanAsync(paths, progress, ct);
            ScanStatus = $"Added {result.Added:N0}, updated {result.Updated:N0}" +
                         (result.Failed > 0 ? $", {result.Failed} failed" : string.Empty);
            ReloadFromDatabase();
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Scan cancelled";
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void ReloadFromDatabase()
    {
        _allRows = _database.GetAllEntries().Select(TrackRow.From).ToList();
        ApplyFilter();
        this.RaisePropertyChanged(nameof(HasTracks));
        this.RaisePropertyChanged(nameof(CountText));
    }

    public IReadOnlyList<string> FilterModes { get; } = ["All Tracks", "Artists", "Albums", "Genres"];

    /// <summary>Legacy browser filter modes are grouping sort orders over the same rows.</summary>
    public string SelectedFilterMode
    {
        get => _selectedFilterMode;
        set
        {
            if (value is null || _selectedFilterMode == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedFilterMode, value);
            ApplyFilter();
            this.RaisePropertyChanged(nameof(CountText));
        }
    }

    private void ApplyFilter()
    {
        var needle = SearchText.Trim();
        IEnumerable<TrackRow> filtered = needle.Length == 0
            ? _allRows
            : _allRows.Where(row => row.Matches(needle));

        filtered = SelectedFilterMode switch
        {
            "Artists" => filtered.OrderBy(r => r.Artist).ThenBy(r => r.Album).ThenBy(r => r.Title),
            "Albums" => filtered.OrderBy(r => r.Album).ThenBy(r => r.Title),
            "Genres" => filtered.OrderBy(r => r.Genre).ThenBy(r => r.Artist).ThenBy(r => r.Title),
            _ => filtered.OrderBy(r => r.Artist).ThenBy(r => r.Album).ThenBy(r => r.Title),
        };

        Rows.Clear();
        foreach (var row in filtered)
        {
            Rows.Add(row);
        }
    }

    // ── Watched folders ─────────────────────────────────────────────────────

    public ObservableCollection<string> WatchedFolders { get; } = new();

    public string? SelectedWatchedFolder { get; set; }

    /// <summary>Loads watched folders from settings, starts watchers, optionally auto-scans.</summary>
    public void InitializeWatchedFolders(bool autoScan)
    {
        if (_settings is null)
        {
            return;
        }

        WatchedFolders.Clear();
        foreach (var folder in _settings.LibraryFolders)
        {
            WatchedFolders.Add(folder);
        }

        _watcher.Watch(_settings.LibraryFolders);

        if (autoScan && _settings.LibraryFolders.Count > 0)
        {
            _ = ScanPathsAsync(_settings.LibraryFolders);
        }
    }

    /// <summary>Registers a watched folder (no scan; callers decide when to scan).</summary>
    public bool AddWatchedFolder(string folder)
    {
        if (_settings is null ||
            _settings.LibraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        _settings.LibraryFolders.Add(folder);
        AppSettingsStore.Save(_settings);
        WatchedFolders.Add(folder);
        _watcher.Watch(_settings.LibraryFolders);
        return true;
    }

    public void RemoveWatchedFolder(string folder)
    {
        if (_settings is null)
        {
            return;
        }

        _settings.LibraryFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        AppSettingsStore.Save(_settings);
        WatchedFolders.Remove(folder);
        _watcher.Watch(_settings.LibraryFolders);
    }

    public Task RescanAsync() =>
        _settings is { LibraryFolders.Count: > 0 }
            ? ScanPathsAsync(_settings.LibraryFolders)
            : Task.CompletedTask;

    // ── BPM + key analysis ──────────────────────────────────────────────────

    private AnalysisWorker? _analysisWorker;
    private int _analyzedSinceReload;

    /// <summary>Raised with each finished analysis so the shell can refresh the beat grid.</summary>
    public event EventHandler<AnalysisResult>? TrackAnalyzed;

    public bool IsAnalysisRunning => _analysisWorker?.IsRunning == true;

    /// <summary>Starts background BPM/key analysis over unanalyzed library tracks.</summary>
    public void StartAnalysis()
    {
        if (IsAnalysisRunning)
        {
            return;
        }

        _analysisWorker = new AnalysisWorker(_database);
        _analysisWorker.TrackAnalyzed += (_, result) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                TrackAnalyzed?.Invoke(this, result);
                if (++_analyzedSinceReload >= 10)
                {
                    _analyzedSinceReload = 0;
                    ReloadFromDatabase();
                }
            });
        _analysisWorker.ProgressChanged += (_, remaining) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                ScanStatus = remaining > 0 ? $"Analyzing: {remaining} left" : string.Empty);
        _analysisWorker.Completed += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ScanStatus = string.Empty;
                ReloadFromDatabase();
            });
        _analysisWorker.Start();
    }

    /// <summary>On-demand analysis of specific files (library/queue context menus).</summary>
    public async Task AnalyzePathsAsync(IReadOnlyList<string> paths)
    {
        var worker = new AnalysisWorker(_database);
        var analyzed = 0;
        foreach (var path in paths.Where(File.Exists))
        {
            ScanStatus = $"Analyzing {System.IO.Path.GetFileName(path)}...";
            var result = await worker.AnalyzeTrackAsync(path);
            if (result is not null)
            {
                analyzed++;
                TrackAnalyzed?.Invoke(this, result);
            }
        }

        ScanStatus = string.Empty;
        if (analyzed > 0)
        {
            ReloadFromDatabase();
        }
    }

    private void OnWatcherFileAdded(string path) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = ScanPathsAsync([path]));

    private void OnWatcherFileRemoved(string path) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _database.Remove(path);
            ReloadFromDatabase();
        });

    private void OnWatcherFileRenamed(string oldPath, string newPath) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _database.Remove(oldPath);
            _ = ScanPathsAsync([newPath]);
        });

    public void Dispose()
    {
        _searchSubscription.Dispose();
        _watcher.Dispose();
        _analysisWorker?.Cancel();
    }
}
