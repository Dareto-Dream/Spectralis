using System.Collections.ObjectModel;
using ReactiveUI;
using Spectralis.App.Services;
using Spectralis.Core.Common;
using Spectralis.Core.Metadata;

namespace Spectralis.App.ViewModels;

/// <summary>One episode row in the Podcasts browser.</summary>
public sealed class PodcastEpisodeRow
{
    public required string Path { get; init; }
    public required string Title { get; init; }
    public required string ShowName { get; init; }
    public uint TrackNumber { get; init; }
    public uint Year { get; init; }
    public double DurationSeconds { get; init; }
    public long ResumePositionMs { get; init; }
    public bool Finished { get; init; }

    public string DurationText => DurationSeconds > 0 ? TimeFormat.FormatSeconds(DurationSeconds) : string.Empty;

    public double ProgressFraction
    {
        get
        {
            if (Finished) return 1.0;
            var durMs = DurationSeconds * 1000.0;
            return durMs > 0 ? Math.Clamp(ResumePositionMs / durMs, 0, 1) : 0;
        }
    }

    public bool HasProgress => ProgressFraction is > 0 and < 1;

    public string StatusText
    {
        get
        {
            if (Finished) return "Finished";
            if (ResumePositionMs <= 3000 || DurationSeconds <= 0) return "New";
            var remainingMinutes = ((DurationSeconds * 1000.0) - ResumePositionMs) / 60000.0;
            return remainingMinutes < 1 ? "Almost done" : $"{(int)Math.Round(remainingMinutes)} min left";
        }
    }

    public static PodcastEpisodeRow From(PodcastEpisodeEntry entry) => new()
    {
        Path = entry.Track.SourcePath,
        Title = entry.Track.DisplayTitle,
        ShowName = string.IsNullOrWhiteSpace(entry.Track.Album)
            ? (string.IsNullOrWhiteSpace(entry.Track.Artist) ? "Unknown show" : entry.Track.Artist)
            : entry.Track.Album,
        TrackNumber = entry.Track.TrackNumber,
        Year = entry.Track.Year,
        DurationSeconds = entry.Track.Duration.TotalSeconds,
        ResumePositionMs = entry.ResumePositionMs,
        Finished = entry.Finished,
    };
}

/// <summary>A podcast show / audiobook — a group of episodes.</summary>
public sealed class PodcastShowRow
{
    public required string ShowName { get; init; }
    public ObservableCollection<PodcastEpisodeRow> Episodes { get; } = new();

    public string EpisodeCountText => Episodes.Count == 1 ? "1 episode" : $"{Episodes.Count} episodes";

    public int UnfinishedCount => Episodes.Count(e => !e.Finished);
}

public sealed class PodcastsViewModel : ViewModelBase, IDisposable
{
    private readonly LibraryDatabase _library;
    private readonly AppSettings _settings;
    private readonly Func<IReadOnlyList<string>, int, Task> _playEpisodes;
    private readonly Action _saveSettings;
    private readonly LibraryWatcher _watcher = new();

    private bool _isScanning;
    private string _scanStatus = string.Empty;
    private PodcastShowRow? _selectedShow;
    private PodcastEpisodeRow? _selectedEpisode;

    public PodcastsViewModel(
        LibraryDatabase library,
        AppSettings settings,
        Func<IReadOnlyList<string>, int, Task> playEpisodes,
        Action saveSettings)
    {
        _library = library;
        _settings = settings;
        _playEpisodes = playEpisodes;
        _saveSettings = saveSettings;

        _watcher.FileAdded += (_, path) => OnFolderChanged();
        _watcher.FileRemoved += (_, path) => OnFolderChanged();
        _watcher.FileRenamed += (_, _) => OnFolderChanged();

        foreach (var folder in _settings.PodcastFolders)
        {
            Folders.Add(folder);
        }

        _watcher.Watch(_settings.PodcastFolders);
        RefreshFromDatabase();
    }

    /// <summary>Designer-only.</summary>
    public PodcastsViewModel()
        : this(
            new LibraryDatabase(Path.Combine(Path.GetTempPath(), $"podcast-designer-{Guid.NewGuid():N}.db")),
            new AppSettings(),
            (_, _) => Task.CompletedTask,
            () => { })
    {
    }

    public ObservableCollection<PodcastShowRow> Shows { get; } = new();

    public ObservableCollection<string> Folders { get; } = new();

    public bool HasShows => Shows.Count > 0;

    public bool HasFolders => Folders.Count > 0;

    public string CountText
    {
        get
        {
            var episodes = Shows.Sum(s => s.Episodes.Count);
            var showLabel = Shows.Count == 1 ? "1 show" : $"{Shows.Count} shows";
            var epLabel = episodes == 1 ? "1 episode" : $"{episodes:N0} episodes";
            return $"{showLabel}  ·  {epLabel}";
        }
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

    public PodcastShowRow? SelectedShow
    {
        get => _selectedShow;
        set => this.RaiseAndSetIfChanged(ref _selectedShow, value);
    }

    public PodcastEpisodeRow? SelectedEpisode
    {
        get => _selectedEpisode;
        set => this.RaiseAndSetIfChanged(ref _selectedEpisode, value);
    }

    public void RefreshFromDatabase()
    {
        var previouslySelectedShow = _selectedShow?.ShowName;

        var episodes = _library.GetPodcastEpisodes()
            .Select(PodcastEpisodeRow.From)
            .ToList();

        Shows.Clear();
        foreach (var group in episodes
                     .GroupBy(e => e.ShowName, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var show = new PodcastShowRow { ShowName = group.Key };
            foreach (var episode in group
                         .OrderBy(e => e.Year)
                         .ThenBy(e => e.TrackNumber)
                         .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase))
            {
                show.Episodes.Add(episode);
            }

            Shows.Add(show);
        }

        SelectedShow = Shows.FirstOrDefault(s => s.ShowName == previouslySelectedShow) ?? Shows.FirstOrDefault();

        this.RaisePropertyChanged(nameof(HasShows));
        this.RaisePropertyChanged(nameof(CountText));
    }

    public Task PlayEpisodeAsync(PodcastEpisodeRow? row) =>
        row is null ? Task.CompletedTask : _playEpisodes(new[] { row.Path }, 0);

    /// <summary>Plays a whole show from its first unfinished episode.</summary>
    public Task PlayShowAsync(PodcastShowRow? show)
    {
        if (show is null || show.Episodes.Count == 0)
        {
            return Task.CompletedTask;
        }

        var paths = show.Episodes.Select(e => e.Path).ToList();
        var startIndex = show.Episodes.ToList().FindIndex(e => !e.Finished);
        return _playEpisodes(paths, startIndex < 0 ? 0 : startIndex);
    }

    public void MarkFinished(PodcastEpisodeRow? row)
    {
        if (row is null) return;
        _library.SaveResume(row.Path, 0, finished: true);
        RefreshFromDatabase();
    }

    public void MarkUnplayed(PodcastEpisodeRow? row)
    {
        if (row is null) return;
        _library.SaveResume(row.Path, 0, finished: false);
        RefreshFromDatabase();
    }

    public void RemoveFromPodcasts(PodcastEpisodeRow? row)
    {
        if (row is null) return;
        _library.SetPodcastOverride(row.Path, false);
        RefreshFromDatabase();
    }

    // ── Folders ─────────────────────────────────────────────────────────────

    public bool AddFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) ||
            _settings.PodcastFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        _settings.PodcastFolders.Add(folder);
        _saveSettings();
        Folders.Add(folder);
        _watcher.Watch(_settings.PodcastFolders);
        this.RaisePropertyChanged(nameof(HasFolders));
        _ = ScanAsync(new[] { folder });
        return true;
    }

    public void RemoveFolder(string folder)
    {
        _settings.PodcastFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        _saveSettings();
        Folders.Remove(folder);
        _watcher.Watch(_settings.PodcastFolders);
        this.RaisePropertyChanged(nameof(HasFolders));
    }

    public Task RescanAsync() =>
        _settings.PodcastFolders.Count > 0 ? ScanAsync(_settings.PodcastFolders) : Task.CompletedTask;

    private async Task ScanAsync(IReadOnlyList<string> folders)
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        try
        {
            var progress = new Progress<LibraryScanProgress>(p =>
                ScanStatus = p.Completed ? string.Empty : $"Scanning {p.Scanned:N0}/{p.Total:N0}...");

            var result = await new LibraryScanner(_library).ScanAsync(
                folders, progress, CancellationToken.None,
                markAsPodcast: true, podcastFolders: _settings.PodcastFolders);

            ScanStatus = $"Added {result.Added:N0}, updated {result.Updated:N0}" +
                         (result.Failed > 0 ? $", {result.Failed} failed" : string.Empty);
            RefreshFromDatabase();
        }
        catch (Exception ex)
        {
            ScanStatus = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void OnFolderChanged() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = RescanAsync());

    public void Dispose() => _watcher.Dispose();
}
