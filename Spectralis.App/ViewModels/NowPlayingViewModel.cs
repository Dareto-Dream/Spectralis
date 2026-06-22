using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.Json;
using ReactiveUI;
using Spectralis.App.Design;
using Spectralis.App.Services;
using NAudio.Wave.SampleProviders;
using Spectralis.Core.Audio;
using Spectralis.Core.Audio.Loopback;
using Spectralis.Core.Audio.Midi;
using Spectralis.Core.Common;
using Spectralis.Core.Embedded;
using Spectralis.Core.Formats;
using Spectralis.Core.Lyrics;
using Spectralis.Core.Metadata;
using Spectralis.Core.ContentWarnings;
using Spectralis.Core.Integrations.Spotify;
using Spectralis.Core.Visualizers;
using Spectralis.Core.Visualizers.Installed;
using Spectralis.Core.Visualizers.Scripting;

namespace Spectralis.App.ViewModels;

/// <summary>One word/segment within a word-timed lyrics line.</summary>
public sealed class LyricSegmentViewModel : ViewModelBase
{
    private bool _isActive;
    private bool _isPast;

    public LyricSegmentViewModel(string text) => Text = text;

    public string Text { get; }

    public bool IsActive
    {
        get => _isActive;
        set => this.RaiseAndSetIfChanged(ref _isActive, value);
    }

    public bool IsPast
    {
        get => _isPast;
        set => this.RaiseAndSetIfChanged(ref _isPast, value);
    }
}

/// <summary>One row in the synced lyrics panel.</summary>
public sealed class LyricLineViewModel : ViewModelBase
{
    private bool _isActive;
    private readonly LyricsLine? _line;

    public LyricLineViewModel(string text, string? explanation, LyricsLine? line = null)
    {
        Text = text;
        Explanation = explanation;
        _line = line;
        if (line?.Segments.Count > 1)
            Segments = line.Segments.Select(s => new LyricSegmentViewModel(s.Text)).ToList();
    }

    public string Text { get; }
    public string? Explanation { get; }
    public bool HasExplanation => !string.IsNullOrWhiteSpace(Explanation);
    public IReadOnlyList<LyricSegmentViewModel>? Segments { get; }
    public bool HasWordTimings => Segments is { Count: > 1 };

    public bool IsActive
    {
        get => _isActive;
        set => this.RaiseAndSetIfChanged(ref _isActive, value);
    }

    public void UpdateSegmentPosition(double posSeconds)
    {
        if (_line is null || Segments is null) return;
        var activeIdx = _line.FindActiveSegmentIndex(posSeconds);
        for (var i = 0; i < Segments.Count; i++)
        {
            Segments[i].IsActive = i == activeIdx;
            Segments[i].IsPast = activeIdx >= 0 && i < activeIdx;
        }
    }
}

/// <summary>Picker entry for the visualizer dropdown.</summary>
public sealed record VisualizerOption(
    string Label,
    VisualizerMode Mode,
    ScriptedVisualizerDefinition? Script = null,
    InstalledVisualizerDefinition? Installed = null)
{
    public override string ToString() => Label;
}

/// <summary>One row in the visible queue panel.</summary>
public sealed class QueueItemViewModel : ViewModelBase
{
    private bool _isCurrent;

    public QueueItemViewModel(int index, string title, string subtitle, bool isCurrent)
    {
        Index = index;
        Path = string.Empty;
        Title = title;
        Subtitle = subtitle;
        IsUrl = false;
        _isCurrent = isCurrent;
    }

    public QueueItemViewModel(int index, string path)
    {
        Index = index;
        Path = path;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            IsUrl = true;
            var lastSeg = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault();
            Title = string.IsNullOrWhiteSpace(lastSeg) ? path : Uri.UnescapeDataString(lastSeg);
            Subtitle = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[4..]
                : uri.Host;
        }
        else
        {
            IsUrl = false;
            Title = System.IO.Path.GetFileNameWithoutExtension(path);
            var folder = System.IO.Path.GetDirectoryName(path);
            Subtitle = string.IsNullOrWhiteSpace(folder)
                ? string.Empty
                : System.IO.Path.GetFileName(folder.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        }
    }

    public int Index { get; }
    public string Path { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public bool IsUrl { get; }
    public string Number => $"{Index + 1}";
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    public bool IsCurrent
    {
        get => _isCurrent;
        set => this.RaiseAndSetIfChanged(ref _isCurrent, value);
    }
}

public sealed class NowPlayingViewModel : ViewModelBase, IDisposable
{
    private readonly AudioEngine _engine;
    private readonly AppSettings _settings;
    private readonly IDisposable? _positionPoll;
    private readonly OpenUrlService _openUrlService;
    private readonly SpotifyService _spotify = new();
    private readonly bool _persistSettings;
    private bool _showVisualizer;
    private VisualizerOption _selectedVisualizer;
    private IReadOnlyList<VisualizerOption> _visualizerOptions = [];
    private LyricsDocument? _lyricsDocument;
    private IVisualizerRenderer? _scriptedVisualizerOverride;
    private bool _isTimedLyrics;
    private LyricLineViewModel? _prevLyricLine;
    private LyricLineViewModel? _currentLyricLine;
    private LyricLineViewModel? _nextLyricLine;
    private string _lyricsSourceLabel = string.Empty;
    private CancellationTokenSource? _spotifyLyricsCts;

    /// <summary>The synced lyrics document for the currently loaded track.</summary>
    public LyricsDocument? CurrentLyrics => _lyricsDocument;

    /// <summary>When set, overrides the catalog-based visualizer with a scripted renderer.</summary>
    public IVisualizerRenderer? ScriptedVisualizerOverride
    {
        get => _scriptedVisualizerOverride;
        set => this.RaiseAndSetIfChanged(ref _scriptedVisualizerOverride, value);
    }

    public bool IsTimedLyrics
    {
        get => _isTimedLyrics;
        private set => this.RaiseAndSetIfChanged(ref _isTimedLyrics, value);
    }

    public LyricLineViewModel? PrevLyricLine
    {
        get => _prevLyricLine;
        private set => this.RaiseAndSetIfChanged(ref _prevLyricLine, value);
    }

    public LyricLineViewModel? CurrentLyricLine
    {
        get => _currentLyricLine;
        private set => this.RaiseAndSetIfChanged(ref _currentLyricLine, value);
    }

    public LyricLineViewModel? NextLyricLine
    {
        get => _nextLyricLine;
        private set => this.RaiseAndSetIfChanged(ref _nextLyricLine, value);
    }

    public string LyricsSourceLabel
    {
        get => _lyricsSourceLabel;
        private set => this.RaiseAndSetIfChanged(ref _lyricsSourceLabel, value);
    }

    private bool _showLyrics;
    private int _activeLyricIndex = -1;
    private readonly ReactiveRuntime _reactiveRuntime = new();
    private bool _isReactiveActive;
    private string _reactiveSectionLabel = string.Empty;

    private bool _hasTrack;
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _album = string.Empty;
    private string _formatBadge = string.Empty;
    private byte[]? _coverArtBytes;
    private bool _isPlaying;
    private double _positionSeconds;
    private double _lengthSeconds;
    private double _volumePercent = 85;
    private string _loadError = string.Empty;
    private string _remoteStatus = string.Empty;
    private string _sourceLabel = string.Empty;
    private bool _isOpeningRemote;
    private string? _remoteAudioTempPath;
    private CancellationTokenSource? _remoteLoadCts;
    private double _volumeBeforeMute = 85;
    private EmbeddedHtmlContext? _embeddedHtml;
    private EmbeddedHtmlContext? _pickedInstalledHtml;
    private EmbeddedVisualizerContext? _embeddedVisualizer;
    private EmbeddedMarkdownContext? _embeddedMarkdown;
    private EmbeddedVideoContext? _embeddedVideo;
    private bool _showEmbeddedHtml;
    private string _youTubeVideoId = string.Empty;
    private bool _showYouTubeVideo;
    private bool _peakHold = true;
    private int _visualizerSensitivityPercent = 100;
    private bool _autoCycleVisualizers;
    private bool _isExporting;
    private bool _showMoreInfo = true;
    private SpotifyTrackState? _spotifyState;
    private CancellationTokenSource? _spotifyArtCts;
    private double _spotifyPositionMs;
    private double _spotifyDurationMs;
    private long _spotifyPositionSetAtTick;
    private WindowsLoopbackCaptureSource? _spotifyLoopback;
    private VisualizerSampleProvider? _spotifyVisualizer;
    private SelectionOption<int> _selectedSampleRate;
    private SelectionOption<int> _selectedCycleDuration;
    private long _nextVisualizerCycleTick;
    private bool _showRemainingTime;
    private bool _showQueue;

    public NowPlayingViewModel(
        AudioEngine engine,
        AppSettings? settings = null,
        bool enablePositionPolling = true)
    {
        _engine = engine;
        _settings = settings is null
            ? new AppSettings()
            : AppSettingsStore.Normalize(settings);
        _persistSettings = settings is not null;
        _openUrlService = new OpenUrlService();
        _openUrlService.SetYtDlpProgressCallback(line =>
        {
            if (OpenUrlService.TryParseYtDlpProgress(line, out var pct))
                RemoteStatus = $"Downloading... {pct}%";
        });
        _visualizerOptions = BuildVisualizerOptions();
        _selectedVisualizer = _visualizerOptions.FirstOrDefault(option => option.Script is null && option.Mode == _settings.CurrentVisualizer)
            ?? _visualizerOptions.FirstOrDefault(option => option.Script is null && option.Mode == _settings.DefaultVisualizer)
            ?? _visualizerOptions.First(option => option.Mode == VisualizerMode.MirrorSpectrum);
        _showVisualizer = _settings.ShowVisualizer;
        _peakHold = _settings.PeakHold;
        _showMoreInfo = _settings.ShowMoreInfo;
        _visualizerSensitivityPercent = _settings.VisualizerSensitivity;
        _autoCycleVisualizers = _settings.EnableVisualizerAutoCycle;
        _volumePercent = _settings.DefaultVolume;
        SampleRateOptions = AppSettingsStore.GetSampleRateOptions();
        CycleDurationOptions = AppSettingsStore.GetCycleDurationOptions();
        _selectedSampleRate = SampleRateOptions.FirstOrDefault(option => option.Value == _settings.PreferredSampleRate)
            ?? SampleRateOptions[0];
        _selectedCycleDuration = CycleDurationOptions.FirstOrDefault(option => option.Value == _settings.VisualizerCycleSeconds)
            ?? CycleDurationOptions.First(option => option.Value == 12);

        _engine.Volume = (float)(_volumePercent / 100.0);
        _engine.SetPreferredSampleRate(_settings.PreferredSampleRate);
        _engine.SetMidiPlaybackInstrument(_settings.MidiInstrument);
        ResetVisualizerCycleDeadline();
        _reactiveRuntime.ParamsChanged += OnReactiveParamsChanged;
        PlaySpotifyCommand = ReactiveCommand.CreateFromTask(PlaySpotifyAsync);

        // TrackEnded arrives on the audio device callback thread; auto-advance on the UI thread.
        _engine.TrackEnded += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = AutoAdvanceAsync());
        _engine.StateMachine.StateChanged += (_, _) => RefreshFromEngine();

        PlayPauseCommand = ReactiveCommand.Create(TogglePlayback);
        StopCommand = ReactiveCommand.Create(StopPlayback);
        NextCommand = ReactiveCommand.CreateFromTask(PlayNextAsync);
        PreviousCommand = ReactiveCommand.CreateFromTask(PlayPreviousAsync);
        NextVisualizerCommand = ReactiveCommand.Create(NextVisualizer);
        PreviousVisualizerCommand = ReactiveCommand.Create(PreviousVisualizer);

        if (enablePositionPolling)
        {
            _positionPoll = Observable
                .Interval(TimeSpan.FromMilliseconds(250))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => RefreshFromEngine());
        }
    }

    /// <summary>Raised after a local file loads into the engine; drives library play counts.</summary>
    public event Action<string>? LocalTrackLoaded;

    /// <summary>
    /// Raised after a remote URL loads successfully. Args: (sourceUrl, title, artist, album, durationSeconds).
    /// Drives scrobbling for Suno, BandLab, YouTube, SoundCloud, and other remote sources.
    /// </summary>
    public event Action<string, string, string, string, double>? RemoteTrackLoaded;

    /// <summary>Raised when the playback session is reset (Stop/Clear Queue). Consumers can use this to clear dependent state.</summary>
    public event EventHandler? SessionReset;

    /// <summary>Raised by a reactive timeline "lyrics" target — shell should navigate to Now Playing and surface the lyrics panel.</summary>
    public event EventHandler? LyricsTargetActivated;

    /// <summary>
    /// Optional callback: given (tags, trackName) → true to proceed with playback, false to abort.
    /// Wired from MainWindow so the UI thread can show the ContentWarningWindow.
    /// </summary>
    public Func<string[], string, Task<bool>>? ContentWarningPrompt { get; set; }

    // ── Beat grid (BPM ticks over the scrubber) ─────────────────────────────

    private double _beatGridBpm;
    private double _beatGridOffsetSeconds;

    public double BeatGridBpm
    {
        get => _beatGridBpm;
        private set
        {
            this.RaiseAndSetIfChanged(ref _beatGridBpm, value);
            this.RaisePropertyChanged(nameof(HasBeatGrid));
        }
    }

    public double BeatGridOffsetSeconds
    {
        get => _beatGridOffsetSeconds;
        private set => this.RaiseAndSetIfChanged(ref _beatGridOffsetSeconds, value);
    }

    public bool HasBeatGrid => _beatGridBpm > 0;

    /// <summary>Applies an analyzed beat grid for the current track (shell-driven).</summary>
    public void SetBeatGrid(double bpm, double firstBeatOffsetSeconds)
    {
        BeatGridOffsetSeconds = Math.Max(0, firstBeatOffsetSeconds);
        BeatGridBpm = Math.Max(0, bpm);
    }

    public void ClearBeatGrid() => SetBeatGrid(0, 0);

    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> NextCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousCommand { get; }
    public ReactiveCommand<Unit, Unit> NextVisualizerCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousVisualizerCommand { get; }

    /// <summary>The engine is exposed for the visualizer host's frame pulls only.</summary>
    public AudioEngine Engine => _engine;

    public PlayQueue Queue { get; } = new();

    public bool HasNext => _spotifyState is not null || Queue.HasNext;
    public bool HasPrevious => _spotifyState is not null || Queue.HasPrevious;

    public ObservableCollection<QueueItemViewModel> QueueItems { get; } = new();

    public bool ShowQueue
    {
        get => _showQueue;
        set => this.RaiseAndSetIfChanged(ref _showQueue, value);
    }

    public bool HasQueueItems => Queue.Count > 0;

    public string QueueHeaderText => Queue.Count == 1
        ? "Queue - 1 track"
        : $"Queue - {Queue.Count} tracks";

    public string QueueUpcomingText
    {
        get
        {
            var upcoming = Queue.CurrentIndex >= 0
                ? Queue.Count - Queue.CurrentIndex - 1
                : Queue.Count;
            return upcoming == 1 ? "1 upcoming" : $"{upcoming} upcoming";
        }
    }

    /// <summary>Jumps playback to a row the user activated in the queue panel.</summary>
    public async Task PlayQueueItemAsync(QueueItemViewModel item)
    {
        if (Queue.SetCurrent(item.Index) is { } path)
        {
            await LoadCurrentQueueTrackAsync(path, startPlayback: true);
        }
    }

    /// <summary>Moves a row directly after the current track ("Play Next").</summary>
    public void PlayQueueItemNext(QueueItemViewModel item)
    {
        if (item.Index == Queue.CurrentIndex)
        {
            return;
        }

        Queue.Remove(item.Index);
        Queue.InsertRange(Queue.CurrentIndex + 1, [item.Path]);
        SyncQueueItems();
    }

    public void RemoveQueueItem(QueueItemViewModel item)
    {
        Queue.Remove(item.Index);
        SyncQueueItems();
    }

    public void MoveQueueItemUp(QueueItemViewModel item)
    {
        Queue.MoveUp(item.Index);
        SyncQueueItems();
    }

    public void MoveQueueItemDown(QueueItemViewModel item)
    {
        Queue.MoveDown(item.Index);
        SyncQueueItems();
    }

    /// <summary>Empties the queue and stops playback, matching the legacy Clear button.</summary>
    public void ClearQueue()
    {
        Queue.Clear();
        _engine.Stop();
        SyncQueueItems();
        RefreshFromEngine();
    }

    /// <summary>Rebuilds the queue rows after a structural change (add/remove/reorder/clear).</summary>
    private void SyncQueueItems()
    {
        QueueItems.Clear();
        var items = Queue.Items;
        for (var index = 0; index < items.Count; index++)
        {
            QueueItems.Add(new QueueItemViewModel(index, items[index])
            {
                IsCurrent = index == Queue.CurrentIndex,
            });
        }

        RaiseQueueNavigationChanged();
    }

    /// <summary>Re-flags the current row when only the playing position in the queue moved.</summary>
    private void SyncQueueCurrent()
    {
        foreach (var item in QueueItems)
        {
            item.IsCurrent = item.Index == Queue.CurrentIndex;
        }

        this.RaisePropertyChanged(nameof(QueueUpcomingText));
    }

    public bool Shuffle
    {
        get => Queue.Shuffle;
        set
        {
            Queue.Shuffle = value;
            this.RaisePropertyChanged();
            RaiseQueueNavigationChanged();
        }
    }

    public RepeatMode Repeat
    {
        get => Queue.Repeat;
        set
        {
            Queue.Repeat = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(RepeatIconData));
            RaiseQueueNavigationChanged();
        }
    }

    public string RepeatIconData => Repeat switch
    {
        RepeatMode.All => IconData.Repeat,
        RepeatMode.One => IconData.RepeatOne,
        _ => IconData.ArrowRight,
    };

    public void CycleRepeat() => Repeat = Repeat switch
    {
        RepeatMode.None => RepeatMode.All,
        RepeatMode.All => RepeatMode.One,
        _ => RepeatMode.None,
    };

    public void TogglePlayback()
    {
        if (_spotifyState is not null && _spotifyHost is not null)
        {
            _ = _spotifyState.IsPaused ? _spotifyHost.ResumeAsync() : _spotifyHost.PauseAsync();
            return;
        }
        _engine.Toggle();
        RefreshFromEngine();
    }

    public void StopPlayback()
    {
        ResetPlaybackSession();
    }

    public void ResetPlaybackSession()
    {
        if (_spotifyState is not null && _spotifyHost is not null)
            _ = _spotifyHost.StopAsync();

        StopSpotifyLoopback();
        _spotifyVisualizer = null;
        _engine.ExternalVisualizerSource = null;

        _remoteLoadCts?.Cancel();
        var oldRemotePath = _remoteAudioTempPath;
        _remoteAudioTempPath = null;

        _engine.Unload();
        RemoteAudioCache.TryDelete(oldRemotePath);

        Queue.Clear();
        SyncQueueItems();
        Shuffle = false;
        Repeat = RepeatMode.None;
        LoadError = string.Empty;
        RemoteStatus = string.Empty;
        SourceLabel = string.Empty;
        IsOpeningRemote = false;
        ApplyTrack(null);
        ApplyLyrics(null);
        _reactiveRuntime.Load(null);
        IsReactiveActive = false;
        ReactiveSectionLabel = string.Empty;
        RaiseQueueNavigationChanged();
        RefreshFromEngine();
        SessionReset?.Invoke(this, EventArgs.Empty);
    }

    public void SeekRelative(double seconds)
    {
        if (_spotifyState is not null && _spotifyHost is not null)
        {
            var elapsed = (!_spotifyState.IsPaused ? (Environment.TickCount64 - _spotifyPositionSetAtTick) : 0L) / 1000.0;
            var current = _spotifyPositionMs / 1000.0 + elapsed;
            var target = Math.Clamp(current + seconds, 0, _spotifyDurationMs / 1000.0);
            _ = _spotifyHost.SeekAsync((int)(target * 1000));
            _spotifyPositionMs = target * 1000;
            _spotifyPositionSetAtTick = Environment.TickCount64;
            return;
        }

        if (!_engine.IsLoaded) return;

        var engineTarget = Math.Clamp(_engine.GetPosition() + seconds, 0, _engine.GetLength());
        _engine.Seek((float)engineTarget);
        if (_reactiveRuntime.IsLoaded)
            _reactiveRuntime.Seek(engineTarget);

        RefreshFromEngine();
    }

    public void AdjustVolume(double deltaPercent) => VolumePercent += deltaPercent;

    public void ToggleMute()
    {
        if (VolumePercent > 0.5)
        {
            _volumeBeforeMute = VolumePercent;
            VolumePercent = 0;
            return;
        }

        VolumePercent = Math.Clamp(_volumeBeforeMute > 0.5 ? _volumeBeforeMute : 85, 1, 100);
    }

    /// <summary>Replaces the queue and starts playback at <paramref name="startIndex"/>.</summary>
    public async Task PlayQueueAsync(IReadOnlyList<string> paths, int startIndex, bool startPlayback = true)
    {
        Queue.Clear();
        Queue.AddRange(paths);
        var path = Queue.SetCurrent(Math.Clamp(startIndex, 0, paths.Count - 1));
        SyncQueueItems();
        if (path is not null)
        {
            await LoadQueueItemAsync(path, startPlayback);
        }
    }

    /// <summary>Adds a remote URL to the queue. If the queue was empty, starts playback immediately.</summary>
    public async Task QueueUrlAsync(string url)
    {
        var wasEmpty = Queue.IsEmpty;
        Queue.Add(url);
        SyncQueueItems();
        if (wasEmpty)
        {
            var path = Queue.SetCurrent(0);
            if (path is not null)
            {
                await LoadQueueItemAsync(path, startPlayback: true);
            }
        }

        RaiseQueueNavigationChanged();
    }

    public async Task QueueFilesAsync(IReadOnlyList<string> paths, bool playIfQueueWasEmpty)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var wasEmpty = Queue.IsEmpty;
        Queue.AddRange(paths);
        SyncQueueItems();
        if (wasEmpty)
        {
            var path = Queue.SetCurrent(0);
            if (path is not null)
            {
                await LoadQueueItemAsync(path, playIfQueueWasEmpty);
            }
        }

        RaiseQueueNavigationChanged();
    }

    /// <summary>Inserts files directly after the current track ("queue next" intent).</summary>
    public async Task QueueFilesNextAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        if (Queue.IsEmpty)
        {
            await QueueFilesAsync(paths, playIfQueueWasEmpty: true);
            return;
        }

        Queue.InsertRange(Queue.CurrentIndex + 1, paths);
        SyncQueueItems();
    }

    public async Task PlayNextAsync()
    {
        if (_spotifyState is not null && _spotifyHost is not null)
        {
            await _spotifyHost.NextTrackAsync();
            return;
        }
        if (Queue.MoveNext() is { } path)
            await LoadQueueItemAsync(path, startPlayback: true);
    }

    public async Task PlayPreviousAsync()
    {
        if (_spotifyState is not null && _spotifyHost is not null)
        {
            if (_spotifyPositionMs > 3000)
                await _spotifyHost.SeekAsync(0);
            else
                await _spotifyHost.PreviousTrackAsync();
            return;
        }
        // Convention: an early prev press restarts the track, not the previous one.
        if (_engine.IsLoaded && _engine.GetPosition() > 3f)
        {
            _engine.Seek(0);
            RefreshFromEngine();
            return;
        }
        if (Queue.MovePrevious() is { } path)
            await LoadQueueItemAsync(path, startPlayback: true);
    }

    private async Task LoadQueueItemAsync(string pathOrUrl, bool startPlayback)
    {
        SyncQueueCurrent();
        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            await LoadUrlAsync(pathOrUrl);
            if (startPlayback && _engine.IsLoaded && !_engine.IsPlaying)
            {
                _engine.Play();
                RefreshFromEngine();
            }
        }
        else
        {
            await LoadCurrentQueueTrackAsync(pathOrUrl, startPlayback);
        }
    }

    private async Task AutoAdvanceAsync()
    {
        if (Queue.HasNext || Queue.Repeat != RepeatMode.None)
        {
            await PlayNextAsync();
        }
        else
        {
            RefreshFromEngine();
        }
    }

    private void RaiseQueueNavigationChanged()
    {
        this.RaisePropertyChanged(nameof(HasNext));
        this.RaisePropertyChanged(nameof(HasPrevious));
        this.RaisePropertyChanged(nameof(HasQueueItems));
        this.RaisePropertyChanged(nameof(QueueHeaderText));
        this.RaisePropertyChanged(nameof(QueueUpcomingText));
    }

    private void RaiseSurfaceModeChanged()
    {
        this.RaisePropertyChanged(nameof(IsSurfaceVisualizer));
        this.RaisePropertyChanged(nameof(IsSurfacePeak));
        this.RaisePropertyChanged(nameof(IsSurfaceEmbedded));
        this.RaisePropertyChanged(nameof(IsSurfaceYouTube));
        this.RaisePropertyChanged(nameof(IsSurfaceOff));
        this.RaisePropertyChanged(nameof(SurfaceModeLabel));
        this.RaisePropertyChanged(nameof(ShowSurfaceExitButton));
        this.RaisePropertyChanged(nameof(ShowVisualizerControls));
    }

    public IReadOnlyList<VisualizerOption> VisualizerOptions
    {
        get => _visualizerOptions;
        private set => this.RaiseAndSetIfChanged(ref _visualizerOptions, value);
    }

    private static IReadOnlyList<VisualizerOption> BuildVisualizerOptions()
    {
        var built = VisualizerCatalog.All
            .Select(d => new VisualizerOption(d.Label, d.Mode))
            .ToList();
        var scripts = ScriptedVisualizerStore.LoadAll()
            .Select(s => new VisualizerOption($"Script: {s.Name}", VisualizerMode.MirrorSpectrum, Script: s))
            .ToList();
        var installed = new InstalledVisualizerStore().LoadAll()
            .Select(d => new VisualizerOption($"Special: {d.DisplayName}", VisualizerMode.MirrorSpectrum, Installed: d))
            .ToList();
        var extras = scripts.Concat(installed).ToList();
        return extras.Count > 0 ? [..built, ..extras] : (IReadOnlyList<VisualizerOption>)built;
    }

    public void RefreshVisualizerOptions()
    {
        var options = BuildVisualizerOptions();
        var prev = _selectedVisualizer;
        VisualizerOptions = options;
        if (prev.Script is { } s)
        {
            var match = options.FirstOrDefault(o => o.Script?.Id == s.Id);
            _selectedVisualizer = match ?? options.First(o => o.Mode == VisualizerMode.MirrorSpectrum);
            this.RaisePropertyChanged(nameof(SelectedVisualizer));
        }
        else if (prev.Installed is { } d)
        {
            var match = options.FirstOrDefault(o => o.Installed?.Id == d.Id);
            _selectedVisualizer = match ?? options.First(o => o.Mode == VisualizerMode.MirrorSpectrum);
            this.RaisePropertyChanged(nameof(SelectedVisualizer));
        }
    }
    public IReadOnlyList<SelectionOption<int>> SampleRateOptions { get; }
    public IReadOnlyList<SelectionOption<int>> CycleDurationOptions { get; }

    public bool ShowVisualizer
    {
        get => _showVisualizer;
        set
        {
            if (_showVisualizer == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _showVisualizer, value);
            _settings.ShowVisualizer = value;
            SaveSettings();
            ResetVisualizerCycleDeadline();
            this.RaisePropertyChanged(nameof(ShowArtworkSurface));
            this.RaisePropertyChanged(nameof(ShowVisualizerSurface));
            this.RaisePropertyChanged(nameof(ShowEmbeddedHtmlSurface));
            RaiseSurfaceModeChanged();
        }
    }

    public bool ShowArtworkSurface => !ShowVisualizer && !ShowYouTubeVideo && !ShowEmbeddedHtml;

    public bool ShowVisualizerSurface => ShowVisualizer && !ShowYouTubeVideo && !ShowEmbeddedHtml;

    public bool ShowEmbeddedHtmlSurface => ShowEmbeddedHtml;

    public bool ShowVisualizerControls => ShowVisualizer || ShowEmbeddedHtml;

    public bool IsSurfaceVisualizer => ShowVisualizer && !PeakHold && !ShowYouTubeVideo && !ShowEmbeddedHtml;

    public bool IsSurfacePeak => ShowVisualizer && PeakHold && !ShowYouTubeVideo && !ShowEmbeddedHtml;

    public bool IsSurfaceEmbedded => ShowEmbeddedHtml;

    public bool IsSurfaceYouTube => ShowYouTubeVideo;

    public bool IsExporting
    {
        get => _isExporting;
        set => this.RaiseAndSetIfChanged(ref _isExporting, value);
    }

    public bool IsSurfaceOff => !ShowVisualizer && !ShowYouTubeVideo && !ShowEmbeddedHtml;

    public bool ShowSurfaceExitButton => ShowYouTubeVideo || ShowEmbeddedHtml;

    public string SurfaceModeLabel
    {
        get
        {
            if (IsSurfaceEmbedded)
            {
                return "HTML";
            }

            if (IsSurfaceYouTube)
            {
                return "YOUTUBE";
            }

            if (IsSurfacePeak)
            {
                return "PEAK";
            }

            return IsSurfaceVisualizer ? "VIZ" : "OFF";
        }
    }

    public EmbeddedHtmlContext? EmbeddedHtml
    {
        get => _embeddedHtml;
        private set
        {
            if (ReferenceEquals(_embeddedHtml, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _embeddedHtml, value);
            this.RaisePropertyChanged(nameof(HasEmbeddedHtml));
            this.RaisePropertyChanged(nameof(HasEmbeddedModules));
            this.RaisePropertyChanged(nameof(EmbeddedStatusText));
            RaiseSurfaceModeChanged();
        }
    }

    public bool HasEmbeddedHtml => EmbeddedHtml is not null;

    public bool HasEmbeddedVisualizer => _embeddedVisualizer is not null;

    public bool HasEmbeddedModules =>
        _embeddedHtml is not null ||
        _embeddedVisualizer is not null ||
        _embeddedMarkdown is not null ||
        _embeddedVideo is not null;

    public string EmbeddedStatusText
    {
        get
        {
            var parts = new List<string>();
            if (_embeddedHtml is not null)
            {
                parts.Add("HTML");
            }

            if (_embeddedVisualizer is not null)
            {
                parts.Add("WASM");
            }

            if (_embeddedMarkdown is not null)
            {
                parts.Add("Markdown");
            }

            if (_embeddedVideo is not null)
            {
                parts.Add("video");
            }

            return parts.Count == 0
                ? string.Empty
                : $"Embedded {string.Join(", ", parts)}";
        }
    }

    public bool ShowEmbeddedHtml
    {
        get => _showEmbeddedHtml;
        set
        {
            if (!HasEmbeddedHtml)
            {
                value = false;
            }

            if (_showEmbeddedHtml == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _showEmbeddedHtml, value);
            this.RaisePropertyChanged(nameof(ShowArtworkSurface));
            this.RaisePropertyChanged(nameof(ShowVisualizerSurface));
            this.RaisePropertyChanged(nameof(ShowEmbeddedHtmlSurface));
            RaiseSurfaceModeChanged();
        }
    }

    public VisualizerOption SelectedVisualizer
    {
        get => _selectedVisualizer;
        set
        {
            if (value is null)
            {
                return;
            }

            if (_selectedVisualizer == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedVisualizer, value);
            if (value.Script is { } script)
            {
                ScriptedVisualizerOverride = new ScriptVisualizerRenderer(script);
                _pickedInstalledHtml = null;
                // Exit HTML surface so the scripted renderer is actually visible.
                if (ShowEmbeddedHtml || !ShowVisualizer)
                {
                    ShowEmbeddedHtml = false;
                    ShowVisualizer = true;
                    RaiseSurfaceModeChanged();
                }
            }
            else if (value.Installed is { } installed)
            {
                ScriptedVisualizerOverride = null;
                var content = new InstalledVisualizerStore().LoadContent(installed.Id);
                _pickedInstalledHtml = content is null ? null : new EmbeddedHtmlContext(
                    content.Id, content.HtmlBytes, content.BinaryAssets, content.TextAssets, content.Version);
                if (_pickedInstalledHtml is not null && _settings.EnableEmbeddedContent)
                {
                    EmbeddedHtml = _pickedInstalledHtml;
                    ShowEmbeddedHtml = true;
                    ShowVisualizer = false;
                    ShowYouTubeVideo = false;
                    RaiseSurfaceModeChanged();
                }
            }
            else
            {
                ScriptedVisualizerOverride = null;
                _pickedInstalledHtml = null;
                // Exit HTML/artwork surface so the selected visualizer is visible.
                if (ShowEmbeddedHtml || !ShowVisualizer)
                {
                    ShowEmbeddedHtml = false;
                    ShowVisualizer = true;
                    RaiseSurfaceModeChanged();
                }
            }
            _settings.CurrentVisualizer = value.Mode;
            SaveSettings();
            ResetVisualizerCycleDeadline();
            this.RaisePropertyChanged(nameof(SelectedVisualizerMode));
        }
    }

    public VisualizerMode SelectedVisualizerMode => _selectedVisualizer.Mode;

    public bool PeakHold
    {
        get => _peakHold;
        set
        {
            if (_peakHold == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _peakHold, value);
            _settings.PeakHold = value;
            SaveSettings();
            RaiseSurfaceModeChanged();
        }
    }

    public bool ShowMoreInfo
    {
        get => _showMoreInfo;
        set
        {
            if (_showMoreInfo == value) return;
            this.RaiseAndSetIfChanged(ref _showMoreInfo, value);
            _settings.ShowMoreInfo = value;
            SaveSettings();
        }
    }

    public int VisualizerSensitivityPercent
    {
        get => _visualizerSensitivityPercent;
        set
        {
            var normalized = Math.Clamp(value, 50, 200);
            if (_visualizerSensitivityPercent == normalized)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _visualizerSensitivityPercent, normalized);
            _settings.VisualizerSensitivity = normalized;
            SaveSettings();
            this.RaisePropertyChanged(nameof(VisualizerSensitivity));
            this.RaisePropertyChanged(nameof(VisualizerSensitivityText));
        }
    }

    public double VisualizerSensitivity => VisualizerSensitivityPercent / 100.0;

    public string VisualizerSensitivityText => $"{VisualizerSensitivityPercent}%";

    public bool AutoCycleVisualizers
    {
        get => _autoCycleVisualizers;
        set
        {
            if (_autoCycleVisualizers == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _autoCycleVisualizers, value);
            _settings.EnableVisualizerAutoCycle = value;
            SaveSettings();
            ResetVisualizerCycleDeadline();
        }
    }

    public SelectionOption<int> SelectedCycleDuration
    {
        get => _selectedCycleDuration;
        set
        {
            if (value is null)
            {
                return;
            }

            if (_selectedCycleDuration == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedCycleDuration, value);
            _settings.VisualizerCycleSeconds = value.Value;
            SaveSettings();
            ResetVisualizerCycleDeadline();
        }
    }

    public SelectionOption<int> SelectedSampleRate
    {
        get => _selectedSampleRate;
        set
        {
            if (value is null)
            {
                return;
            }

            if (_selectedSampleRate == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedSampleRate, value);
            _settings.PreferredSampleRate = value.Value;
            _engine.SetPreferredSampleRate(value.Value);
            SaveSettings();
            this.RaisePropertyChanged(nameof(OutputRateText));
        }
    }

    public string OutputRateText => _engine.IsLoaded && _engine.EffectiveSampleRate > 0
        ? $"{_engine.EffectiveSampleRate / 1000d:0.#} kHz"
        : SelectedSampleRate.Label;

    /// <summary>True when a .spectralis-reactive.json sidecar is driving this track.</summary>
    public bool IsReactiveActive
    {
        get => _isReactiveActive;
        private set => this.RaiseAndSetIfChanged(ref _isReactiveActive, value);
    }

    public string ReactiveSectionLabel
    {
        get => _reactiveSectionLabel;
        private set => this.RaiseAndSetIfChanged(ref _reactiveSectionLabel, value);
    }

    public ReactiveRuntime ReactiveRuntime => _reactiveRuntime;

    public ObservableCollection<LyricLineViewModel> LyricsLines { get; } = new();

    public bool HasLyrics => LyricsLines.Count > 0;

    public bool HasAnnotations => LyricsLines.Any(l => l.HasExplanation);

    /// <summary>Local file path of the currently playing track, or null for remote/URL sources.</summary>
    public string? CurrentTrackPath => _engine.CurrentTrack?.SourcePath is { } p && File.Exists(p) ? p : null;

    public bool ShowLyrics
    {
        get => _showLyrics;
        set => this.RaiseAndSetIfChanged(ref _showLyrics, value);
    }

    /// <summary>Index of the active synced line; -1 before the first line.</summary>
    public int ActiveLyricIndex
    {
        get => _activeLyricIndex;
        private set
        {
            if (_activeLyricIndex == value)
            {
                return;
            }

            if (_activeLyricIndex >= 0 && _activeLyricIndex < LyricsLines.Count)
            {
                LyricsLines[_activeLyricIndex].IsActive = false;
            }

            this.RaiseAndSetIfChanged(ref _activeLyricIndex, value);

            if (value >= 0 && value < LyricsLines.Count)
            {
                LyricsLines[value].IsActive = true;
            }

            RefreshCarouselLines();
        }
    }

    private void RefreshCarouselLines()
    {
        PrevLyricLine    = _activeLyricIndex > 0 && _activeLyricIndex <= LyricsLines.Count
                            ? LyricsLines[_activeLyricIndex - 1] : null;
        CurrentLyricLine = _activeLyricIndex >= 0 && _activeLyricIndex < LyricsLines.Count
                            ? LyricsLines[_activeLyricIndex] : null;
        NextLyricLine    = _activeLyricIndex >= 0 && _activeLyricIndex + 1 < LyricsLines.Count
                            ? LyricsLines[_activeLyricIndex + 1] : null;
    }

    public bool HasTrack
    {
        get => _hasTrack;
        private set => this.RaiseAndSetIfChanged(ref _hasTrack, value);
    }

    public string Title
    {
        get => _title;
        private set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public string Artist
    {
        get => _artist;
        private set => this.RaiseAndSetIfChanged(ref _artist, value);
    }

    public string Album
    {
        get => _album;
        private set => this.RaiseAndSetIfChanged(ref _album, value);
    }

    /// <summary>"FLAC / 44.1 kHz / 1024 kbps" style line, rendered in the data face.</summary>
    public string FormatBadge
    {
        get => _formatBadge;
        private set => this.RaiseAndSetIfChanged(ref _formatBadge, value);
    }

    public byte[]? CoverArtBytes
    {
        get => _coverArtBytes;
        private set => this.RaiseAndSetIfChanged(ref _coverArtBytes, value);
    }

    public void OverrideCurrentTrackDisplay(string title, string artist, string album, byte[]? coverArtBytes)
    {
        if (!HasTrack)
            return;

        Title = title;
        Artist = artist;
        Album = album;
        CoverArtBytes = coverArtBytes;
        this.RaisePropertyChanged(nameof(PlayPauseMenuLabel));
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => this.RaiseAndSetIfChanged(ref _isPlaying, value);
    }

    public string PlayPauseIconData => IsPlaying ? IconData.Pause : IconData.Play;

    /// <summary>Two-way slider binding. A set that diverges from the engine is a seek.</summary>
    public double PositionSeconds
    {
        get => _positionSeconds;
        set
        {
            if (_spotifyState is not null && _spotifyHost is not null)
            {
                if (Math.Abs(value - _spotifyPositionMs / 1000.0) > 1.0)
                {
                    _ = _spotifyHost.SeekAsync((int)(value * 1000));
                    _spotifyPositionMs = value * 1000;
                    _spotifyPositionSetAtTick = Environment.TickCount64;
                }
            }
            else if (Math.Abs(value - _engine.GetPosition()) > 1.0)
            {
                _engine.Seek((float)value);
                if (_reactiveRuntime.IsLoaded)
                    _reactiveRuntime.Seek(value);
            }

            this.RaiseAndSetIfChanged(ref _positionSeconds, value);
            this.RaisePropertyChanged(nameof(PositionText));
        }
    }

    public double LengthSeconds
    {
        get => _lengthSeconds;
        private set => this.RaiseAndSetIfChanged(ref _lengthSeconds, value);
    }

    /// <summary>Elapsed time, or remaining time as "-m:ss" after a time-label click.</summary>
    public string PositionText => _showRemainingTime && _engine.IsLoaded
        ? $"-{TimeFormat.FormatSeconds(Math.Max(0, _lengthSeconds - _positionSeconds))}"
        : TimeFormat.FormatSeconds(_positionSeconds);

    public string LengthText => TimeFormat.FormatSeconds(_lengthSeconds);

    public bool ShowRemainingTime
    {
        get => _showRemainingTime;
        set
        {
            this.RaiseAndSetIfChanged(ref _showRemainingTime, value);
            this.RaisePropertyChanged(nameof(PositionText));
        }
    }

    public void ToggleTimeDisplay() => ShowRemainingTime = !ShowRemainingTime;

    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (Math.Abs(_volumePercent - normalized) < 0.01)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _volumePercent, normalized);
            _engine.Volume = (float)(normalized / 100.0);
            _settings.DefaultVolume = (int)Math.Round(normalized);
            SaveSettings();
            this.RaisePropertyChanged(nameof(IsMuted));
            this.RaisePropertyChanged(nameof(MuteMenuLabel));
            this.RaisePropertyChanged(nameof(VolumeLabel));
        }
    }

    public bool IsMuted => _volumePercent <= 0.5;

    public string MuteMenuLabel => IsMuted ? "Unmute" : "Mute";

    /// <summary>Compact volume caption; legacy swaps "VOL" for "Muted" at zero.</summary>
    public string VolumeLabel => IsMuted ? "MUTED" : "VOL";

    /// <summary>Playback-menu label parity: Open Audio... / Play / Pause.</summary>
    public string PlayPauseMenuLabel => !HasTrack ? "Open Audio..." : IsPlaying ? "Pause" : "Play";

    public string LoadError
    {
        get => _loadError;
        private set => this.RaiseAndSetIfChanged(ref _loadError, value);
    }

    public string RemoteStatus
    {
        get => _remoteStatus;
        private set => this.RaiseAndSetIfChanged(ref _remoteStatus, value);
    }

    public string SourceLabel
    {
        get => _sourceLabel;
        private set => this.RaiseAndSetIfChanged(ref _sourceLabel, value);
    }

    private SpotifyPlaybackHostService? _spotifyHost;
    /// <summary>Set by MainWindow once the hidden Spotify Web Playback SDK host is ready (it owns
    /// the WebView, which must live in the View's visual tree — see SpotifyPlaybackHostService).</summary>
    public SpotifyPlaybackHostService? SpotifyHost
    {
        get => _spotifyHost;
        set
        {
            if (_spotifyHost is not null)
                _spotifyHost.TrackStateChanged -= OnSpotifyStateChanged;
            _spotifyHost = value;
            if (_spotifyHost is not null)
                _spotifyHost.TrackStateChanged += OnSpotifyStateChanged;
        }
    }

    private void OnSpotifyStateChanged(object? sender, SpotifyTrackState state)
        => _ = ApplySpotifyStateAsync(state);

    private async Task ApplySpotifyStateAsync(SpotifyTrackState state)
    {
        var isNewTrack = _spotifyState?.TrackId != state.TrackId || _spotifyState?.Name != state.Name;

        _spotifyState = state;
        _spotifyPositionMs = state.PositionMs;
        _spotifyDurationMs = state.DurationMs;
        _spotifyPositionSetAtTick = Environment.TickCount64;

        HasTrack = true;
        Title = state.Name;
        Artist = state.Artist;
        Album = state.Album;
        FormatBadge = "Spotify";
        IsPlaying = !state.IsPaused;
        this.RaisePropertyChanged(nameof(PlayPauseIconData));
        this.RaisePropertyChanged(nameof(PlayPauseMenuLabel));
        this.RaisePropertyChanged(nameof(HasNext));
        this.RaisePropertyChanged(nameof(HasPrevious));

        // Start loopback when playing, stop when paused
        if (!state.IsPaused)
            EnsureSpotifyLoopbackRunning();
        else
            StopSpotifyLoopback();

        // Fetch art and lyrics only on track changes
        if (isNewTrack)
        {
            _spotifyArtCts?.Cancel();
            var cts = _spotifyArtCts = new CancellationTokenSource();
            CoverArtBytes = state.AlbumArtUrl is not null
                ? await FetchSpotifyArtAsync(state.AlbumArtUrl, cts.Token)
                : null;

            if (!cts.IsCancellationRequested && _spotifyHost is not null)
                _ = RefreshSpotifyQueueAsync();

            // Fetch timed lyrics from Spotify relay
            _spotifyLyricsCts?.Cancel();
            var lyricsCts = _spotifyLyricsCts = new CancellationTokenSource();
            ApplyLyrics(null);
            if (!string.IsNullOrEmpty(state.TrackId))
            {
                var lyrics = await SpotifyLyricsService.FetchAsync(state.TrackId, lyricsCts.Token);
                if (!lyricsCts.IsCancellationRequested && lyrics is not null)
                    ApplyLyrics(lyrics);
            }
        }
    }

    private async Task RefreshSpotifyQueueAsync()
    {
        if (_spotifyHost is null) return;
        var snapshot = await _spotifyHost.GetQueueAsync();
        if (_spotifyState is null || snapshot is null) return;

        QueueItems.Clear();
        var current = snapshot.Current;
        if (current is not null)
            QueueItems.Add(new QueueItemViewModel(0, current.Name ?? "", BuildSpotifySubtitle(current), isCurrent: true));
        var i = 1;
        foreach (var track in snapshot.Queue.Take(50))
            QueueItems.Add(new QueueItemViewModel(i++, track.Name ?? "", BuildSpotifySubtitle(track), isCurrent: false));
        this.RaisePropertyChanged(nameof(HasQueueItems));
        this.RaisePropertyChanged(nameof(QueueHeaderText));
        this.RaisePropertyChanged(nameof(QueueUpcomingText));
    }

    private static string BuildSpotifySubtitle(SpotifyPlaybackTrack track) =>
        string.IsNullOrWhiteSpace(track.Artist) ? (track.Album ?? "") :
        string.IsNullOrWhiteSpace(track.Album)  ? track.Artist :
        $"{track.Artist} — {track.Album}";

    private void EnsureSpotifyLoopbackRunning()
    {
        if (_spotifyLoopback is not null || !OperatingSystem.IsWindows()) return;
        if (_spotifyVisualizer is null)
        {
            _spotifyVisualizer = new VisualizerSampleProvider(new SignalGenerator(44100, 2) { Gain = 0 });
            _engine.ExternalVisualizerSource = _spotifyVisualizer;
        }
        _spotifyLoopback = new WindowsLoopbackCaptureSource();
        var started = _spotifyLoopback.Start(_spotifyVisualizer);
        AppLogPaths.AppendTimestamped(SpotifyPlaybackHostService.SpotifyLogPath,
            started ? $"Loopback capture started" : "Loopback capture failed");
    }

    private void StopSpotifyLoopback()
    {
        _spotifyLoopback?.Stop();
        _spotifyLoopback?.Dispose();
        _spotifyLoopback = null;
    }

    private static async Task<byte[]?> FetchSpotifyArtAsync(string url, CancellationToken ct)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            return await http.GetByteArrayAsync(url, ct);
        }
        catch { return null; }
    }

    public bool IsSpotifyLinked => _spotify.IsLinked;

    public ReactiveCommand<Unit, Unit> PlaySpotifyCommand { get; }

    private async Task PlaySpotifyAsync()
    {
        if (SpotifyHost is null)
        {
            RemoteStatus = "Spotify playback host is not ready.";
            return;
        }

        RemoteStatus = "Connecting to Spotify...";
        var started = await SpotifyHost.PlayAsync();
        RemoteStatus = started ? "Spotify playback requested" : SpotifyHost.StatusMessage ?? "Spotify playback failed";
    }

    public bool IsOpeningRemote
    {
        get => _isOpeningRemote;
        private set => this.RaiseAndSetIfChanged(ref _isOpeningRemote, value);
    }

    public string YouTubeVideoId
    {
        get => _youTubeVideoId;
        private set
        {
            if (_youTubeVideoId == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _youTubeVideoId, value);
            this.RaisePropertyChanged(nameof(HasYouTubeVideo));
            RaiseSurfaceModeChanged();
        }
    }

    public bool HasYouTubeVideo => !string.IsNullOrWhiteSpace(YouTubeVideoId);

    public bool ShowYouTubeVideo
    {
        get => _showYouTubeVideo;
        set
        {
            if (!HasYouTubeVideo)
            {
                value = false;
            }

            if (_showYouTubeVideo == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _showYouTubeVideo, value);
            this.RaisePropertyChanged(nameof(ShowArtworkSurface));
            this.RaisePropertyChanged(nameof(ShowVisualizerSurface));
            this.RaisePropertyChanged(nameof(ShowEmbeddedHtmlSurface));
            RaiseSurfaceModeChanged();
        }
    }

    public void UseVisualizerSurface()
    {
        ShowEmbeddedHtml = false;
        ShowYouTubeVideo = false;
        PeakHold = false;
        ShowVisualizer = true;
        RaiseSurfaceModeChanged();
    }

    public void UsePeakSurface()
    {
        ShowEmbeddedHtml = false;
        ShowYouTubeVideo = false;
        PeakHold = true;
        ShowVisualizer = true;
        RaiseSurfaceModeChanged();
    }

    public void UseEmbeddedHtmlSurface()
    {
        if (!HasEmbeddedHtml)
        {
            return;
        }

        ShowYouTubeVideo = false;
        ShowVisualizer = false;
        ShowEmbeddedHtml = true;
        RaiseSurfaceModeChanged();
    }

    public void UseYouTubeSurface()
    {
        if (!HasYouTubeVideo)
        {
            return;
        }

        ShowEmbeddedHtml = false;
        ShowVisualizer = false;
        ShowYouTubeVideo = true;
        RaiseSurfaceModeChanged();
    }

    public void UseArtworkSurface()
    {
        ShowEmbeddedHtml = false;
        ShowYouTubeVideo = false;
        ShowVisualizer = false;
        RaiseSurfaceModeChanged();
    }

    public void CycleSurfaceMode()
    {
        if (IsSurfaceVisualizer)
        {
            UsePeakSurface();
            return;
        }

        if (IsSurfacePeak)
        {
            if (HasEmbeddedHtml)
            {
                UseEmbeddedHtmlSurface();
                return;
            }

            if (HasYouTubeVideo)
            {
                UseYouTubeSurface();
                return;
            }

            UseArtworkSurface();
            return;
        }

        if (IsSurfaceEmbedded)
        {
            if (HasYouTubeVideo)
            {
                UseYouTubeSurface();
                return;
            }

            UseArtworkSurface();
            return;
        }

        if (IsSurfaceYouTube)
        {
            UseArtworkSurface();
            return;
        }

        UseVisualizerSurface();
    }

    /// <summary>Loads and starts a single local file, replacing the queue with it.</summary>
    public async Task LoadTrackAsync(string path)
    {
        Queue.Clear();
        Queue.Add(path);
        Queue.SetCurrent(0);
        SyncQueueItems();
        await LoadCurrentQueueTrackAsync(path, _settings.AutoPlayOnOpen);
    }

    public async Task LoadPreparedTrackAsync(
        string path,
        TrackInfo trackInfo,
        bool startPlayback,
        bool ownsTemporaryFile = false)
    {
        Queue.Clear();
        Queue.Add(path);
        Queue.SetCurrent(0);
        SyncQueueItems();

        LoadError = string.Empty;
        RemoteStatus = string.Empty;
        ClearYouTubeVideo();
        _remoteLoadCts?.Cancel();

        var oldRemotePath = _remoteAudioTempPath;
        _remoteAudioTempPath = ownsTemporaryFile ? path : null;

        try
        {
            await Task.Run(() => _engine.Load(path, trackInfo));
            if (startPlayback)
            {
                _engine.Play();
            }

            RemoteAudioCache.TryDelete(oldRemotePath);
            ApplyTrack(_engine.CurrentTrack);
            ApplyLyrics(null);
            _reactiveRuntime.Load(null);
            IsReactiveActive = false;
            ReactiveSectionLabel = string.Empty;
        }
        catch (Exception ex)
        {
            if (ownsTemporaryFile)
            {
                RemoteAudioCache.TryDelete(path);
                _remoteAudioTempPath = null;
            }

            LoadError = ex.Message;
            RemoteAudioCache.TryDelete(oldRemotePath);
            ApplyTrack(null);
            ApplyLyrics(null);
            _reactiveRuntime.Load(null);
            IsReactiveActive = false;
            ReactiveSectionLabel = string.Empty;
        }

        SyncQueueCurrent();
        RaiseQueueNavigationChanged();
        RefreshFromEngine();
    }

    public async Task LoadUrlAsync(string input)
    {
        _remoteLoadCts?.Cancel();
        _remoteLoadCts?.Dispose();
        _remoteLoadCts = new CancellationTokenSource();
        var cancellationToken = _remoteLoadCts.Token;

        Queue.Clear();
        SyncQueueItems();
        LoadError = string.Empty;
        RemoteStatus = "Resolving remote source...";
        IsOpeningRemote = true;

        RemoteAudioResolveResult resolved;
        try
        {
            resolved = await _openUrlService.ResolveAsync(input, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            RemoteStatus = string.Empty;
            IsOpeningRemote = false;
            return;
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            RemoteStatus = string.Empty;
            IsOpeningRemote = false;
            return;
        }

        await LoadResolvedAsync(resolved, cancellationToken);
    }

    /// <summary>
    /// Fast path for clipboard-detected links that were already validated and
    /// downloaded ahead of time (see MainWindow's clipboard monitor): skips
    /// re-resolving so Play starts instantly instead of redoing the network/yt-dlp work.
    /// </summary>
    public async Task LoadPreResolvedUrlAsync(RemoteAudioResolveResult resolved)
    {
        _remoteLoadCts?.Cancel();
        _remoteLoadCts?.Dispose();
        _remoteLoadCts = new CancellationTokenSource();
        var cancellationToken = _remoteLoadCts.Token;

        Queue.Clear();
        SyncQueueItems();
        LoadError = string.Empty;
        IsOpeningRemote = true;

        await LoadResolvedAsync(resolved, cancellationToken);
    }

    private async Task LoadResolvedAsync(RemoteAudioResolveResult resolved, CancellationToken cancellationToken)
    {
        string? cachedPath = null;
        try
        {
            // WebView widget fallback: embed the platform player directly (SoundCloud, Suno, Spotify).
            if (resolved.IsWebViewFallback())
            {
                var htmlBytes = System.Text.Encoding.UTF8.GetBytes(resolved.WebViewEmbedHtml!);
                EmbeddedHtml = new EmbeddedHtmlContext(
                    resolved.Kind.ToString(),
                    htmlBytes,
                    new Dictionary<string, byte[]>(),
                    null,
                    null);
                UseEmbeddedHtmlSurface();
                RemoteStatus = $"Playing {resolved.ServiceLabel} via embedded widget.";
                SourceLabel = resolved.ServiceLabel;
                IsOpeningRemote = false;
                return;
            }

            if (!string.IsNullOrWhiteSpace(resolved.CachedAudioPath))
            {
                RemoteStatus = $"Preparing {resolved.ServiceLabel} audio...";
                cachedPath = resolved.CachedAudioPath;
            }
            else
            {
                var serviceLabel = resolved.ServiceLabel;
                RemoteStatus = $"Caching {serviceLabel} audio...";
                cachedPath = await RemoteAudioCache.DownloadAsync(
                    resolved.AudioUrl,
                    resolved.DownloadExtension,
                    cancellationToken,
                    requestInitialRange: true,
                    referer: resolved.RefererUrl ?? resolved.SourceUrl,
                    progress: new Progress<int>(pct => RemoteStatus = $"Caching {serviceLabel} audio... {pct}%"));
            }

            TrackInfo metadata = new() { SourcePath = cachedPath };
            await Task.Run(() => metadata = TrackMetadataReader.Read(cachedPath), cancellationToken);
            var artworkBytes = metadata.CoverArt ??
                await OpenUrlService.TryFetchArtworkBytesAsync(resolved.ArtworkUrl, cancellationToken);

            var trackInfo = metadata with
            {
                Title = FirstNonEmpty(
                    metadata.Title,
                    resolved.Title,
                    Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(new Uri(resolved.SourceUrl).AbsolutePath)),
                    resolved.ServiceLabel),
                Artist = FirstNonEmpty(metadata.Artist, resolved.Artist),
                Album = FirstNonEmpty(metadata.Album, resolved.Album, resolved.ServiceLabel),
                Duration = metadata.Duration > TimeSpan.Zero ? metadata.Duration : resolved.Duration,
                FormatName = FirstNonEmpty(metadata.FormatName, resolved.FormatName),
                CoverArt = artworkBytes,
                CoverArtMimeType = metadata.CoverArt is not null ? metadata.CoverArtMimeType : null,
            };

            _engine.Load(cachedPath, trackInfo);
            var oldRemotePath = _remoteAudioTempPath;
            _remoteAudioTempPath = cachedPath;
            cachedPath = null;
            RemoteAudioCache.TryDelete(oldRemotePath);

            if (_settings.AutoPlayOnOpen)
            {
                _engine.Play();
            }

            // Pre-open the next queue track (if local) while the remote track starts playing.
            var nextQueuePath = Queue.PeekNextPath();
            if (!string.IsNullOrEmpty(nextQueuePath) && File.Exists(nextQueuePath))
                _ = _engine.PrepareNextAsync(nextQueuePath);

            ApplyTrack(_engine.CurrentTrack);
            ApplyYouTubeVideo(resolved);
            var remoteLyrics = !string.IsNullOrWhiteSpace(resolved.LyricsText)
                ? LrcParser.ParsePlainText(resolved.LyricsText, resolved.ServiceLabel)
                : null;
            ApplyLyrics(remoteLyrics);
            _reactiveRuntime.Load(null);
            IsReactiveActive = false;
            ReactiveSectionLabel = string.Empty;
            RemoteStatus = $"Opened {resolved.ServiceLabel}.";
            SourceLabel = resolved.ServiceLabel;
            RemoteTrackLoaded?.Invoke(
                resolved.SourceUrl,
                trackInfo.Title,
                trackInfo.Artist,
                trackInfo.Album,
                trackInfo.Duration.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            RemoteAudioCache.TryDelete(cachedPath);
            RemoteStatus = string.Empty;
        }
        catch (Exception ex)
        {
            RemoteAudioCache.TryDelete(cachedPath);
            LoadError = ex.Message;
            RemoteStatus = string.Empty;
        }
        finally
        {
            IsOpeningRemote = false;
            RefreshFromEngine();
        }
    }

    private async Task LoadCurrentQueueTrackAsync(string path, bool startPlayback)
    {
        LoadError = string.Empty;
        RemoteStatus = string.Empty;
        ClearBeatGrid();
        ClearYouTubeVideo();
        _remoteLoadCts?.Cancel();
        var oldRemotePath = _remoteAudioTempPath;
        _remoteAudioTempPath = null;
        try
        {
            LyricsDocument? lyrics = null;
            ReactiveTimelineDocument? reactive = null;
            TrackInfo? metadata = null;
            bool seamless = false;
            await Task.Run(() =>
            {
                metadata = TrackMetadataReader.Read(path);
                seamless = _engine.TrySeamlessAdvance(path, metadata);
                if (!seamless)
                    _engine.Load(path, metadata);
                lyrics = LyricsLoader.LoadForTrack(path);
                reactive = ReactiveTimelineLoader.LoadSidecar(path);
            });

            if (!seamless && startPlayback && await ShouldPlayWithContentWarningAsync(path))
            {
                _engine.Play();
            }
            else if (seamless && startPlayback)
            {
                // TrySeamlessAdvance preserves the playing state; no extra Play() needed.
            }
            RemoteAudioCache.TryDelete(oldRemotePath);
            ApplyTrack(_engine.CurrentTrack);
            ApplyLyrics(lyrics);
            _reactiveRuntime.Load(reactive);
            IsReactiveActive = _reactiveRuntime.IsLoaded;
            ReactiveSectionLabel = string.Empty;
            LocalTrackLoaded?.Invoke(path);

            // Pre-open the next queue track in the background to minimize cold-start latency.
            var nextPath = Queue.PeekNextPath();
            if (!string.IsNullOrEmpty(nextPath) && File.Exists(nextPath))
                _ = _engine.PrepareNextAsync(nextPath);
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            RemoteAudioCache.TryDelete(oldRemotePath);
            ApplyTrack(null);
            ApplyLyrics(null);
            _reactiveRuntime.Load(null);
            IsReactiveActive = false;
            ReactiveSectionLabel = string.Empty;
        }

        SyncQueueCurrent();
        RaiseQueueNavigationChanged();
        RefreshFromEngine();
    }

    public void RefreshFromEngine()
    {
        if (_spotifyState is null)
        {
            IsPlaying = _engine.IsPlaying;
            this.RaisePropertyChanged(nameof(PlayPauseIconData));
            this.RaisePropertyChanged(nameof(PlayPauseMenuLabel));
        }

        double length, position;
        if (_spotifyState is not null)
        {
            length = _spotifyDurationMs / 1000.0;
            var elapsed = (!_spotifyState.IsPaused ? (Environment.TickCount64 - _spotifyPositionSetAtTick) : 0L) / 1000.0;
            position = Math.Min(_spotifyPositionMs / 1000.0 + elapsed, length);
        }
        else
        {
            length = _engine.GetLength();
            position = _engine.GetPosition();
        }

        LengthSeconds = length;
        this.RaisePropertyChanged(nameof(LengthText));

        if (Math.Abs(position - _positionSeconds) > 0.05)
        {
            _positionSeconds = Math.Min(position, length);
            this.RaisePropertyChanged(nameof(PositionSeconds));
            this.RaisePropertyChanged(nameof(PositionText));
        }

        if (_engine.CurrentTrack is null && HasTrack && _spotifyState is null)
        {
            ApplyTrack(null);
        }

        if (_lyricsDocument is not null && !_lyricsDocument.IsDescription)
        {
            ActiveLyricIndex = _lyricsDocument.FindLineIndex(position);
            if (_activeLyricIndex >= 0 && _activeLyricIndex < LyricsLines.Count)
                LyricsLines[_activeLyricIndex].UpdateSegmentPosition(position);
        }

        if (_reactiveRuntime.IsLoaded)
        {
            _reactiveRuntime.Advance(position);
            ReactiveSectionLabel = _reactiveRuntime.CurrentSection?.Label ?? string.Empty;
        }

        this.RaisePropertyChanged(nameof(OutputRateText));
        CycleVisualizerIfDue();
    }

    private void OnReactiveParamsChanged(object? sender, ReactiveParamsChangedEventArgs e)
    {
        switch (e.Target.ToLowerInvariant())
        {
            case "theme":
                if (_settings.UseEmbeddedTrackThemes &&
                    TryGetReactiveString(e.Params, "mode", out var tMode) &&
                    TryGetReactiveString(e.Params, "accent", out var tAccent) &&
                    Enum.TryParse<AppThemeMode>(tMode, ignoreCase: true, out var applyMode) &&
                    Enum.TryParse<AppThemeAccent>(tAccent, ignoreCase: true, out var applyAccent) &&
                    Enum.IsDefined(applyMode) && Enum.IsDefined(applyAccent))
                {
                    AppThemeService.Apply(applyMode, applyAccent);
                }
                break;

            case "visualizer":
                if (TryGetReactiveString(e.Params, "mode", out var vMode) &&
                    Enum.TryParse<VisualizerMode>(vMode, ignoreCase: true, out var vizMode) &&
                    Enum.IsDefined(vizMode))
                {
                    var opt = _visualizerOptions.FirstOrDefault(o => o.Script is null && o.Mode == vizMode);
                    if (opt is not null)
                        SelectedVisualizer = opt;
                }
                break;

            case "lyrics":
                LyricsTargetActivated?.Invoke(this, EventArgs.Empty);
                break;

            case "shader":
                SpectralisLog.Info("Reactive timeline: 'shader' target received — WASM execution not yet available.");
                break;
        }
    }

    private static bool TryGetReactiveString(IReadOnlyDictionary<string, object?> dict, string key, out string value)
    {
        if (dict.TryGetValue(key, out var raw) && raw is not null)
        {
            if (raw is JsonElement { ValueKind: JsonValueKind.String } je)
            {
                value = je.GetString() ?? string.Empty;
                return !string.IsNullOrEmpty(value);
            }
            value = raw.ToString() ?? string.Empty;
            return !string.IsNullOrEmpty(value);
        }
        value = string.Empty;
        return false;
    }

    private void ApplyLyrics(LyricsDocument? document)
    {
        _lyricsDocument = document;
        ActiveLyricIndex = -1;
        LyricsLines.Clear();

        if (document is not null)
        {
            foreach (var line in document.Lines)
            {
                LyricsLines.Add(new LyricLineViewModel(line.Text, line.Explanation, line));
            }
        }

        IsTimedLyrics = document is not null && !document.IsDescription;
        LyricsSourceLabel = document?.SourceLabel ?? string.Empty;

        PrevLyricLine = null;
        CurrentLyricLine = null;
        NextLyricLine = null;

        this.RaisePropertyChanged(nameof(HasLyrics));
        this.RaisePropertyChanged(nameof(HasAnnotations));
        ShowLyrics = LyricsLines.Count > 0;
    }

    public async Task RefreshCurrentTrackMetadataAsync(string filePath)
    {
        var currentTrack = _engine.CurrentTrack;
        if (currentTrack is null) return;
        if (!string.Equals(currentTrack.SourcePath, filePath, StringComparison.OrdinalIgnoreCase)) return;

        var refreshed = await Task.Run(() => TrackMetadataReader.Read(filePath));
        ApplyTrack(refreshed);
    }

    private void ApplyTrack(TrackInfo? track)
    {
        var wasSpotify = _spotifyState is not null;
        _spotifyState = null;
        _spotifyArtCts?.Cancel();
        _spotifyLyricsCts?.Cancel();
        StopSpotifyLoopback();
        _spotifyVisualizer = null;
        _engine.ExternalVisualizerSource = null;
        if (wasSpotify)
        {
            this.RaisePropertyChanged(nameof(HasNext));
            this.RaisePropertyChanged(nameof(HasPrevious));
        }

        if (track is null)
        {
            HasTrack = false;
            Title = string.Empty;
            Artist = string.Empty;
            Album = string.Empty;
            FormatBadge = string.Empty;
            CoverArtBytes = null;
            ClearBeatGrid();
            ClearYouTubeVideo();
            ClearEmbeddedModules();
            this.RaisePropertyChanged(nameof(PlayPauseMenuLabel));
            return;
        }

        HasTrack = true;
        Title = track.DisplayTitle;
        Artist = track.Artist;
        Album = track.Album;
        CoverArtBytes = track.CoverArt;
        ApplyEmbeddedModules(track);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(track.FormatName))
        {
            parts.Add(track.FormatName);
        }

        if (track.SampleRateHz > 0)
        {
            parts.Add($"{track.SampleRateHz / 1000.0:0.#} kHz");
        }

        if (track.BitrateKbps > 0)
        {
            parts.Add($"{track.BitrateKbps} kbps");
        }

        FormatBadge = string.Join(" / ", parts);
        this.RaisePropertyChanged(nameof(PlayPauseMenuLabel));
    }

    private async Task<bool> ShouldPlayWithContentWarningAsync(string path)
    {
        if (ContentWarningPrompt is null) return true;
        var tags = TrackContentWarningStore.Get(path);
        if (tags.Length == 0) return true;
        var name = Path.GetFileNameWithoutExtension(path);
        return await ContentWarningPrompt(tags, name);
    }

    private void ApplyEmbeddedModules(TrackInfo track)
    {
        this.RaiseAndSetIfChanged(ref _embeddedVisualizer, track.EmbeddedVisualizer);
        this.RaiseAndSetIfChanged(ref _embeddedMarkdown, track.EmbeddedMarkdown);
        this.RaiseAndSetIfChanged(ref _embeddedVideo, track.EmbeddedVideo);
        EmbeddedHtml = track.EmbeddedHtml;

        if (_settings.UseEmbeddedTrackThemes && track.EmbeddedTheme is { } theme &&
            Enum.TryParse<AppThemeMode>(theme.Mode, ignoreCase: true, out var themeMode) &&
            Enum.TryParse<AppThemeAccent>(theme.Accent, ignoreCase: true, out var themeAccent) &&
            Enum.IsDefined(themeMode) && Enum.IsDefined(themeAccent))
        {
            AppThemeService.Apply(themeMode, themeAccent);
        }

        this.RaisePropertyChanged(nameof(HasEmbeddedVisualizer));
        this.RaisePropertyChanged(nameof(HasEmbeddedModules));
        this.RaisePropertyChanged(nameof(EmbeddedStatusText));

        // Markdown/video fall back to the HTML surface: convert once and promote.
        if (EmbeddedHtml is null && _embeddedMarkdown is not null && _settings.EnableEmbeddedContent)
        {
            EmbeddedHtml = EmbeddedMarkdownRenderer.ToHtmlContext(_embeddedMarkdown);
        }

        if (EmbeddedHtml is null && _embeddedVideo is not null && _settings.EnableEmbeddedContent)
        {
            EmbeddedHtml = EmbeddedVideoRenderer.ToHtmlContext(_embeddedVideo);
        }

        if (HasEmbeddedHtml && _settings.EnableEmbeddedContent)
        {
            UseEmbeddedHtmlSurface();
        }
        else if (ShowEmbeddedHtml)
        {
            ShowEmbeddedHtml = false;
        }

        RaiseSurfaceModeChanged();
    }

    private void ClearEmbeddedModules()
    {
        if (_pickedInstalledHtml is not null && _settings.EnableEmbeddedContent)
        {
            // A user-picked installed HTML visualizer survives track changes.
            EmbeddedHtml = _pickedInstalledHtml;
            ShowEmbeddedHtml = true;
        }
        else
        {
            ShowEmbeddedHtml = false;
            EmbeddedHtml = null;
        }
        this.RaiseAndSetIfChanged(ref _embeddedVisualizer, null);
        this.RaiseAndSetIfChanged(ref _embeddedMarkdown, null);
        this.RaiseAndSetIfChanged(ref _embeddedVideo, null);
        this.RaisePropertyChanged(nameof(HasEmbeddedVisualizer));
        this.RaisePropertyChanged(nameof(HasEmbeddedModules));
        this.RaisePropertyChanged(nameof(EmbeddedStatusText));
        AppThemeService.Apply(_settings);
        RaiseSurfaceModeChanged();
    }

    private void ApplyYouTubeVideo(RemoteAudioResolveResult resolved)
    {
        if (resolved.Kind == RemoteAudioServiceKind.YouTube &&
            !string.IsNullOrWhiteSpace(resolved.ExternalId))
        {
            YouTubeVideoId = resolved.ExternalId;
            ShowYouTubeVideo = false;
            return;
        }

        ClearYouTubeVideo();
    }

    private void ClearYouTubeVideo()
    {
        ShowYouTubeVideo = false;
        YouTubeVideoId = string.Empty;
    }

    public void NextVisualizer()
    {
        if (VisualizerOptions.Count <= 1)
        {
            return;
        }

        var currentIndex = GetSelectedVisualizerIndex();
        SelectedVisualizer = VisualizerOptions[(currentIndex + 1) % VisualizerOptions.Count];
    }

    public void PreviousVisualizer()
    {
        if (VisualizerOptions.Count <= 1)
        {
            return;
        }

        var currentIndex = GetSelectedVisualizerIndex();
        SelectedVisualizer = VisualizerOptions[currentIndex <= 0 ? VisualizerOptions.Count - 1 : currentIndex - 1];
    }

    private int GetSelectedVisualizerIndex()
    {
        // Compare by value equality first so script/installed options (which all
        // share Mode=MirrorSpectrum) are not confused with the regular catalog entry.
        for (var index = 0; index < VisualizerOptions.Count; index++)
        {
            if (VisualizerOptions[index] == _selectedVisualizer)
                return index;
        }

        // Fallback: match by mode for plain catalog entries.
        for (var index = 0; index < VisualizerOptions.Count; index++)
        {
            if (VisualizerOptions[index].Mode == _selectedVisualizer.Mode &&
                VisualizerOptions[index].Script is null &&
                VisualizerOptions[index].Installed is null)
                return index;
        }

        return 0;
    }

    public void ApplyMidiInstrument(MidiPlaybackInstrument instrument)
    {
        var normalized = MidiPlaybackInstrumentCatalog.Normalize(instrument);
        _settings.MidiInstrument = normalized;
        _engine.SetMidiPlaybackInstrument(normalized);
        SaveSettings();
    }

    public void ApplyDefaultVisualizer(VisualizerMode mode)
    {
        _settings.DefaultVisualizer = VisualizerCatalog.All.Any(definition => definition.Mode == mode)
            ? mode
            : VisualizerMode.MirrorSpectrum;
        SaveSettings();
    }

    private void CycleVisualizerIfDue()
    {
        if (!AutoCycleVisualizers ||
            !ShowVisualizer ||
            !_engine.IsLoaded ||
            !_engine.IsPlaying ||
            IsSurfaceEmbedded ||
            ShowYouTubeVideo ||
            IsExporting ||
            VisualizerOptions.Count <= 1)
        {
            ResetVisualizerCycleDeadline();
            return;
        }

        if (Environment.TickCount64 < _nextVisualizerCycleTick)
        {
            return;
        }

        NextVisualizer();
        ResetVisualizerCycleDeadline();
    }

    private void ResetVisualizerCycleDeadline() =>
        _nextVisualizerCycleTick = Environment.TickCount64 + (SelectedCycleDuration.Value * 1000L);

    private void SaveSettings()
    {
        if (_persistSettings)
        {
            AppSettingsStore.Save(_settings);
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    public void Dispose()
    {
        _positionPoll?.Dispose();
        _remoteLoadCts?.Cancel();
        _remoteLoadCts?.Dispose();
        RemoteAudioCache.TryDelete(_remoteAudioTempPath);
    }
}
