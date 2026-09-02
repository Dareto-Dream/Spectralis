using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.Json;
using ReactiveUI;
using Spectralis.App.Design;
using Spectralis.App.Services;
using NAudio.Wave.SampleProviders;
using Spectralis.Core.Audio;
using Spectralis.Core.Audio.Effects;
using Spectralis.Core.Audio.Loopback;
using Spectralis.Core.Audio.Midi;
using Spectralis.Core.Common;
using Spectralis.Core.Embedded;
using Spectralis.Core.Formats;
using Spectralis.Core.Lyrics;
using Spectralis.Core.Metadata;
using Spectralis.Core.Scrobbling;
using Spectralis.Core.ContentWarnings;
using Spectralis.Core.Integrations.Spotify;
using Spectralis.Core.Playlists;
using Spectralis.Core.Visualizers;
using Spectralis.Core.Visualizers.Installed;
using Spectralis.Core.Visualizers.Scripting;
using Spectralis.Core.SongWars;

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

    /// <summary>Known display metadata (a Spotify track's real title/artist, from the playlist
    /// that queued it) — used instead of trying to derive anything from the raw entry, which for
    /// a "spotify:track:..." uri has no meaningful filename/host to extract at all.</summary>
    public QueueItemViewModel(int index, string path, string title, string subtitle)
    {
        Index = index;
        Path = path;
        IsUrl = false;
        Title = title;
        Subtitle = subtitle;
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

    /// <summary>App-wide dead zones (Settings → Streamer) that positioned HUD overlays avoid.</summary>
    public IReadOnlyList<Spectralis.Core.Layout.DeadZone> DeadZones => _settings.DeadZones;

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
    private bool _showSongWarsPanel;
    private bool _showNotepadPanel;
    private bool _showMetronomePanel;
    private bool _showEffectsChainPanel;
    private SongWarsSessionController? _songWarsSession;
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
    // Queued visualizer/markdown/video surface waiting behind a story explainer — see
    // TryAdvancePastStory(). Null once consumed or when the track has no story.
    private EmbeddedHtmlContext? _embeddedHtmlAfterStory;
    private EmbeddedHtmlContext? _pickedInstalledHtml;
    // True when the *current track itself* (not a user-picked "Special:" entry) carries
    // embedded HTML/WASM/Markdown/video content — see IsVisualizerLocked.
    private bool _trackHasEmbeddedSurface;
    // Album world HTML pinned across track changes so the interactive map stays live.
    private EmbeddedHtmlContext? _pinnedAlbumWorldHtml;
    private string? _albumWorldDir;
    private bool _albumWorldShowingWorld;
    private string _albumWorldCurrentTrackId = string.Empty;
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
    /// <summary>True when the currently-playing Spotify track was reached via Spectralis's own
    /// Queue (e.g. a mixed local+Spotify playlist) rather than the standalone "Play Spotify"
    /// entry point. Next/Previous/auto-advance must stay Queue-driven in that case instead of
    /// deferring to Spotify's own context queue, since the next Queue entry may be a local file.</summary>
    private bool _queueDrivenSpotifyTrack;
    /// <summary>True from the moment a Stop is requested until the next Spotify play command
    /// fires. StopSpotifyPlayback's PauseAsync call is fire-and-forget, and the SDK still reports
    /// a player_state_changed event once that pause actually lands — without this guard, that late
    /// event reaches ApplySpotifyStateAsync after ResetPlaybackSession has already cleared
    /// everything and resurrects _spotifyState/HasTrack, making Stop look like it needs pressing
    /// twice (the first press "undoes itself"; the second sticks only because no further event
    /// arrives after an already-paused player).</summary>
    private bool _spotifyStopRequested;
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
    private ListeningActivitySnapshot _idleActivity = ListeningActivitySnapshot.Empty;
    private readonly IDisposable? _idleActivityTick;

    public NowPlayingViewModel(
        AudioEngine engine,
        AppSettings? settings = null,
        bool enablePositionPolling = true,
        EffectChain? effectChain = null)
    {
        _engine = engine;
        EffectsChain = new EffectsChainViewModel(effectChain ?? new EffectChain());
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
        Notepads.NotepadsAvailableForCurrentTrack += () => ShowNotepadPanel = true;
        PlaySpotifyCommand = ReactiveCommand.CreateFromTask(PlaySpotifyAsync);

        // TrackEnded arrives on the audio device callback thread; auto-advance on the UI thread.
        _engine.TrackEnded += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = AutoAdvanceAsync());
        _engine.StateMachine.StateChanged += (_, _) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(RefreshFromEngine);

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

        // Surfaced on the "nothing playing" empty state so it doesn't read as a
        // dead screen — same source data the status bar already had, just visible.
        RefreshIdleActivity();
        _idleActivityTick = Avalonia.Threading.DispatcherTimer.Run(
            () =>
            {
                RefreshIdleActivity();
                return true;
            },
            TimeSpan.FromSeconds(30));
    }

    /// <summary>Raised after a local file loads into the engine; drives library play counts.</summary>
    public event Action<string>? LocalTrackLoaded;

    /// <summary>
    /// Raised after a remote URL loads successfully. Args: (sourceUrl, title, artist, album, durationSeconds).
    /// Drives scrobbling for Suno, BandLab, YouTube, SoundCloud, and other remote sources.
    /// </summary>
    public event Action<string, string, string, string, double>? RemoteTrackLoaded;

    /// <summary>Raised with the on-disk path of the queue's next-up track as soon as it's
    /// known, so listeners further up the pipeline (Shared Play) can pre-upload it.</summary>
    public event Action<string>? UpcomingQueueTrackReady;

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

    /// <summary>Known (Title, Artist) for queue entries that aren't real file paths — set via
    /// <see cref="SetQueueTrackMetadata"/> right before queueing a playlist that has richer
    /// metadata than the raw path/uri can express (a Spotify track's actual name, say).</summary>
    private Dictionary<string, (string Title, string Subtitle)> _queueTrackMetadata = [];

    public bool HasNext => _spotifyState is not null || Queue.HasNext;
    public bool HasPrevious => _spotifyState is not null || Queue.HasPrevious;

    public ObservableCollection<QueueItemViewModel> QueueItems { get; } = new();

    /// <summary>Call immediately before PlayQueueAsync/QueueFilesAsync with the same entries so
    /// the Queue panel can show real titles for non-file entries instead of deriving something
    /// from the raw path/uri (which for a Spotify track is just an opaque id, not a name).</summary>
    public void SetQueueTrackMetadata(IReadOnlyDictionary<string, (string Title, string Artist)> metadata)
    {
        _queueTrackMetadata = metadata.ToDictionary(kv => kv.Key, kv => (kv.Value.Title, kv.Value.Artist));
    }

    public bool ShowQueue
    {
        get => _showQueue;
        set
        {
            this.RaiseAndSetIfChanged(ref _showQueue, value);
            this.RaisePropertyChanged(nameof(AnyPanelOpen));
        }
    }

    public bool HasQueueItems => Queue.Count > 0;

    public bool ShowSongWarsPanel
    {
        get => _showSongWarsPanel;
        set
        {
            this.RaiseAndSetIfChanged(ref _showSongWarsPanel, value);
            this.RaisePropertyChanged(nameof(AnyPanelOpen));
        }
    }

    public NotepadsViewModel Notepads { get; } = new();

    public bool ShowNotepadPanel
    {
        get => _showNotepadPanel;
        set
        {
            this.RaiseAndSetIfChanged(ref _showNotepadPanel, value);
            this.RaisePropertyChanged(nameof(AnyPanelOpen));
        }
    }

    /// <summary>True when any of the docked side panels (lyrics/queue/notes/song wars/metronome/effects) is open — drives the collapsed panel-rail button's active state.</summary>
    public bool AnyPanelOpen =>
        ShowLyrics || ShowQueue || ShowNotepadPanel || ShowSongWarsPanel || ShowMetronomePanel || ShowEffectsChainPanel;

    public EffectsChainViewModel EffectsChain { get; }

    public bool ShowMetronomePanel
    {
        get => _showMetronomePanel;
        set
        {
            this.RaiseAndSetIfChanged(ref _showMetronomePanel, value);
            this.RaisePropertyChanged(nameof(AnyPanelOpen));
        }
    }

    public bool ShowEffectsChainPanel
    {
        get => _showEffectsChainPanel;
        set
        {
            this.RaiseAndSetIfChanged(ref _showEffectsChainPanel, value);
            this.RaisePropertyChanged(nameof(AnyPanelOpen));
        }
    }

    public SongWarsSessionController? SongWarsSession
    {
        get => _songWarsSession;
        set { _songWarsSession = value; NotifySongWarsChanged(); }
    }

    public Action? SongWarsPopOutRequested { get; set; }

    /// <summary>Raised when LoadUrlAsync fails to resolve/download a remote source (e.g.
    /// yt-dlp blocked from a video that disables embedding). View shows a friendly
    /// "can't play this" prompt with Open in Browser / Cancel instead of a raw error.
    /// Args: (source URL to offer opening, technical error message for the log).</summary>
    public Action<string, string>? RemoteLoadFailedRequested { get; set; }

    public bool SongWarsHasSession => _songWarsSession is not null;
    public string SongWarsTournamentName => _songWarsSession?.Tournament.Name ?? "";
    public string SongWarsTrackAName => _songWarsSession?.CurrentTrackA?.DisplayTitle ?? "—";
    public string SongWarsTrackAArtist => _songWarsSession?.CurrentTrackA?.ArtistDisplayName ?? "";
    public string SongWarsTrackBName => _songWarsSession?.CurrentTrackB?.DisplayTitle ?? "—";
    public string SongWarsTrackBArtist => _songWarsSession?.CurrentTrackB?.ArtistDisplayName ?? "";

    public string SongWarsMatchStatusText
    {
        get
        {
            var match = _songWarsSession?.CurrentMatch;
            if (match is null) return _songWarsSession is null ? "" : "Tournament complete";
            return $"{match.Bracket}  ·  {match.RoundId}  ·  {match.Phase}";
        }
    }

    public string SongWarsPhaseText
    {
        get
        {
            var phase = _songWarsSession?.CurrentMatch?.Phase;
            return phase switch
            {
                SongWarsMatchPhase.TrackAPlaying => "● Track A Playing",
                SongWarsMatchPhase.TrackBPlaying => "● Track B Playing",
                SongWarsMatchPhase.PrimaryVoting => "● Voting",
                SongWarsMatchPhase.EliminationVoting => "● Elimination Vote",
                SongWarsMatchPhase.Reveal => "Revealed",
                SongWarsMatchPhase.Paused => "⏸ Paused",
                SongWarsMatchPhase.Complete => "Complete",
                SongWarsMatchPhase.Skipped => "Skipped",
                _ => ""
            };
        }
    }

    public string SongWarsTallyText
    {
        get
        {
            if (_songWarsSession?.CurrentMatch is null) return "";
            try
            {
                var t = _songWarsSession.TallyCurrentLive();
                return $"Pass: {t.PassCount}  Fail: {t.FailCount}  Elim: {t.EliminatedCount}  ({t.SubmittedJudgeCount}/{_songWarsSession.Tournament.Judges.Count} submitted)";
            }
            catch { return ""; }
        }
    }

    public bool SongWarsHasOutcome
    {
        get
        {
            var match = _songWarsSession?.CurrentMatch;
            return match?.Phase == SongWarsMatchPhase.Reveal && match.VoteSnapshots.LastOrDefault() is not null;
        }
    }

    public string SongWarsOutcomeText
    {
        get
        {
            var match = _songWarsSession?.CurrentMatch;
            if (match?.Phase != SongWarsMatchPhase.Reveal) return "";
            var snap = match.VoteSnapshots.LastOrDefault();
            return snap is null ? "" : $"Result: {snap.Outcome}";
        }
    }

    public string SongWarsOutcomeDetail
    {
        get
        {
            var match = _songWarsSession?.CurrentMatch;
            if (match?.Phase != SongWarsMatchPhase.Reveal) return "";
            return match.VoteSnapshots.LastOrDefault()?.Explanation ?? "";
        }
    }

    public void NotifySongWarsChanged()
    {
        this.RaisePropertyChanged(nameof(SongWarsHasSession));
        this.RaisePropertyChanged(nameof(SongWarsTournamentName));
        this.RaisePropertyChanged(nameof(SongWarsTrackAName));
        this.RaisePropertyChanged(nameof(SongWarsTrackAArtist));
        this.RaisePropertyChanged(nameof(SongWarsTrackBName));
        this.RaisePropertyChanged(nameof(SongWarsTrackBArtist));
        this.RaisePropertyChanged(nameof(SongWarsMatchStatusText));
        this.RaisePropertyChanged(nameof(SongWarsPhaseText));
        this.RaisePropertyChanged(nameof(SongWarsTallyText));
        this.RaisePropertyChanged(nameof(SongWarsHasOutcome));
        this.RaisePropertyChanged(nameof(SongWarsOutcomeText));
        this.RaisePropertyChanged(nameof(SongWarsOutcomeDetail));
    }

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
            var path = items[index];
            var row = _queueTrackMetadata.TryGetValue(path, out var meta)
                ? new QueueItemViewModel(index, path, meta.Title, meta.Subtitle)
                : new QueueItemViewModel(index, path);
            row.IsCurrent = index == Queue.CurrentIndex;
            QueueItems.Add(row);
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
        // ApplyTrack(null) below already stops Spotify's real device (if it was the active
        // source) and clears the loopback/visualizer/state bookkeeping that used to be
        // duplicated here — duplicating it was how this and the "switch to a local track"
        // path could disagree about whether Spotify still needed stopping.
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
        if (_spotifyState is not null && _spotifyHost is not null && !_queueDrivenSpotifyTrack)
        {
            await _spotifyHost.NextTrackAsync();
            return;
        }
        if (Queue.MoveNext() is { } path)
            await LoadQueueItemAsync(path, startPlayback: true);
    }

    public async Task PlayPreviousAsync()
    {
        if (_spotifyState is not null && _spotifyHost is not null && !_queueDrivenSpotifyTrack)
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
        if (pathOrUrl.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            await LoadSpotifyQueueTrackAsync(pathOrUrl, startPlayback);
        }
        else if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            _queueDrivenSpotifyTrack = false;
            await LoadUrlAsync(pathOrUrl);
            if (startPlayback && _engine.IsLoaded && !_engine.IsPlaying)
            {
                _engine.Play();
                RefreshFromEngine();
            }
        }
        else
        {
            _queueDrivenSpotifyTrack = false;
            await LoadCurrentQueueTrackAsync(pathOrUrl, startPlayback);
        }
    }

    /// <summary>Plays a single Spotify track as the current Queue entry (a mixed local+Spotify
    /// playlist), rather than deferring to Spotify's own context/queue the way the standalone
    /// "Play Spotify" flow does. Stops the local engine first — <see cref="AudioEngine.Stop"/>
    /// (not Pause) also zeroes its reported position, so a subsequent Previous press doesn't
    /// mistake the stale local track for "recently playing" and seek it instead of moving the Queue.</summary>
    private async Task LoadSpotifyQueueTrackAsync(string trackUri, bool startPlayback)
    {
        _queueDrivenSpotifyTrack = true;
        _engine.Stop();

        if (!startPlayback || _spotifyHost is null)
        {
            return;
        }

        _spotifyStopRequested = false;
        RemoteStatus = "Connecting to Spotify...";
        var started = await _spotifyHost.PlayUriAsync(trackUri);
        RemoteStatus = started ? "Spotify playback requested" : _spotifyHost.StatusMessage ?? "Spotify playback failed";
    }

    private async Task AutoAdvanceAsync()
    {
        if (_spotifyState is not null && !_queueDrivenSpotifyTrack)
        {
            RefreshFromEngine();
            return;
        }

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
        this.RaisePropertyChanged(nameof(IsAlbumWorldActive));
        this.RaisePropertyChanged(nameof(IsAlbumWorldShowingWorld));
        this.RaisePropertyChanged(nameof(IsNilState));
        this.RaisePropertyChanged(nameof(HasTrackOrAlbumWorld));
        this.RaisePropertyChanged(nameof(IsSurfaceVisualizer));
        this.RaisePropertyChanged(nameof(IsSurfacePeak));
        this.RaisePropertyChanged(nameof(IsSurfaceEmbedded));
        this.RaisePropertyChanged(nameof(IsSurfaceYouTube));
        this.RaisePropertyChanged(nameof(IsSurfaceOff));
        this.RaisePropertyChanged(nameof(SurfaceModeLabel));
        this.RaisePropertyChanged(nameof(ShowSurfaceExitButton));
        this.RaisePropertyChanged(nameof(ShowVisualizerControls));
        this.RaisePropertyChanged(nameof(EmbeddedStatusText));
        this.RaisePropertyChanged(nameof(IsVisualizerLocked));
    }

    public IReadOnlyList<VisualizerOption> VisualizerOptions
    {
        get => _visualizerOptions;
        private set => this.RaiseAndSetIfChanged(ref _visualizerOptions, value);
    }

    /// <summary>Instance method (not static) because the catalog entries it filters out —
    /// Spinning Disk/Album Cover (RequiresAlbumArt) and Piano Roll (RequiresMidi) — depend on
    /// the currently loaded track. Scripts/installed visualizers carry no such requirement and
    /// are always included.</summary>
    private IReadOnlyList<VisualizerOption> BuildVisualizerOptions()
    {
        var hasAlbumArt = CoverArtBytes is not null;
        var hasMidi = _spotifyState is null && _engine.IsMidiLoaded;
        var built = VisualizerCatalog.All
            .Where(d => (!d.RequiresAlbumArt || hasAlbumArt) && (!d.RequiresMidi || hasMidi))
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
        else if (!options.Contains(prev))
        {
            // Plain catalog pick (Album Cover/Spinning Disk/Piano Roll) whose RequiresAlbumArt/
            // RequiresMidi gate no longer holds for the current track — same fallback as above,
            // so the picker's selection never points at an entry it no longer lists.
            _selectedVisualizer = options.First(o => o.Mode == VisualizerMode.MirrorSpectrum);
            this.RaisePropertyChanged(nameof(SelectedVisualizer));
        }
    }

    /// <summary>Applies a playlist's saved default visualizer, if it still resolves against the
    /// live catalog/script/installed list — a script or installed visualizer can be deleted after
    /// being set as a default, so a miss here is silently ignored rather than treated as an error.</summary>
    public void ApplyDefaultVisualizer(VisualizerRef? reference)
    {
        if (reference is null)
        {
            return;
        }

        var match = VisualizerOptions.FirstOrDefault(option => reference.Kind switch
        {
            VisualizerRefKind.Scripted => option.Script?.Id == reference.Id,
            VisualizerRefKind.Installed => option.Installed?.Id == reference.Id,
            _ => option.Script is null && option.Installed is null && option.Mode == reference.Mode,
        });

        if (match is not null)
        {
            SelectedVisualizer = match;
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

    /// <summary>Called when the embedded surface requests playback resume (spectral.resume()).
    /// If a visualizer/markdown/video surface is queued behind a story explainer, swap to it
    /// now instead of leaving the story on screen — this is the "VN talking, then visualizer"
    /// handoff for capsules that declare both. No-ops (returns false) when nothing is queued,
    /// e.g. every normal pause/resume during regular playback.</summary>
    public bool TryAdvancePastStory()
    {
        if (_embeddedHtmlAfterStory is not { } next)
        {
            return false;
        }

        _embeddedHtmlAfterStory = null;
        EmbeddedHtml = next;
        return true;
    }

    public bool HasEmbeddedVisualizer => _embeddedVisualizer is not null;

    /// <summary>The current track's own embedded video, if it carries one — offered as a
    /// video-export visualizer source.</summary>
    public Spectralis.Core.Embedded.EmbeddedVideoContext? TrackEmbeddedVideo => _embeddedVideo;

    /// <summary>The HTML visualizer surface currently in play for this track — the capsule's
    /// own HTML, or a user-picked "Special:" installed one. Offered as a video-export source.</summary>
    public Spectralis.Core.Embedded.EmbeddedHtmlContext? TrackEmbeddedHtml => _embeddedHtml ?? _pickedInstalledHtml;

    /// <summary>True when a `.spectralis` capsule or `.spectral` album world track brought its
    /// own HTML/WASM visualizer (or Markdown/video promoted to the HTML surface) — the picker,
    /// prev/next, and keyboard shortcuts are all blocked while this is true, since the capsule
    /// owns the surface. Does not apply to a user-picked "Special:" installed HTML visualizer —
    /// that's a normal picker choice, not capsule content, and stays switchable.</summary>
    public bool IsVisualizerLocked => IsAlbumWorldActive || _trackHasEmbeddedSurface;

    public bool HasEmbeddedModules =>
        _embeddedHtml is not null ||
        _embeddedVisualizer is not null ||
        _embeddedMarkdown is not null ||
        _embeddedVideo is not null;

    public string EmbeddedStatusText
    {
        get
        {
            if (_pinnedAlbumWorldHtml is not null)
                return "Album world";

            var parts = new List<string>();
            if (_embeddedHtml is not null)
                parts.Add("HTML");
            if (_embeddedVisualizer is not null)
                parts.Add("WASM");
            if (_embeddedMarkdown is not null)
                parts.Add("Markdown");
            if (_embeddedVideo is not null)
                parts.Add("video");

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

            // Capsule/album-world embedded visualizer owns the surface — block picker,
            // prev/next, and keyboard-shortcut changes until it's gone. NextVisualizer()/
            // PreviousVisualizer() both funnel through this setter, so one guard covers all
            // three entry points (ComboBox binding, toolbar buttons, comma/period shortcuts).
            if (IsVisualizerLocked)
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
        set
        {
            this.RaiseAndSetIfChanged(ref _showLyrics, value);
            this.RaisePropertyChanged(nameof(AnyPanelOpen));
        }
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
        private set
        {
            this.RaiseAndSetIfChanged(ref _hasTrack, value);
            this.RaisePropertyChanged(nameof(IsNilState));
            this.RaisePropertyChanged(nameof(HasTrackOrAlbumWorld));
        }
    }

    /// <summary>True when there is nothing to show — no track and no album world map.</summary>
    public bool IsNilState => !HasTrack && !IsAlbumWorldActive;

    /// <summary>OLED users chose true black on purpose — decorative backdrops back off there.</summary>
    public bool IsOledTheme => _settings.ThemeMode == AppThemeMode.Oled;

    /// <summary>True when there's real scrobble history to show on the empty state.</summary>
    public bool HasIdleActivityStats => _idleActivity.HasHistory;

    public bool HasIdleStreak => _idleActivity.CurrentStreakDays > 1;

    public string IdleListensText => _idleActivity.TotalScrobbles.ToString("N0");

    public string IdleHoursText => _idleActivity.TotalHours >= 10
        ? _idleActivity.TotalHours.ToString("0")
        : _idleActivity.TotalHours.ToString("0.#");

    public string IdleStreakText => _idleActivity.CurrentStreakDays == 1
        ? "1 day"
        : $"{_idleActivity.CurrentStreakDays} days";

    /// <summary>One telemetry line for the empty state — same convention as the
    /// library's BPM/key/kbps columns, not a separate "stat card" component.</summary>
    public string IdleActivitySummaryText
    {
        get
        {
            var parts = new List<string> { $"{IdleListensText} listens", $"{IdleHoursText}h logged" };
            if (HasIdleStreak)
            {
                parts.Add($"{IdleStreakText} streak");
            }

            return string.Join("   ·   ", parts);
        }
    }

    private void RefreshIdleActivity()
    {
        var next = ListeningActivitySnapshot.FromHistory(ScrobbleQueue.LoadHistory());
        if (next == _idleActivity)
        {
            return;
        }

        _idleActivity = next;
        this.RaisePropertyChanged(nameof(HasIdleActivityStats));
        this.RaisePropertyChanged(nameof(HasIdleStreak));
        this.RaisePropertyChanged(nameof(IdleListensText));
        this.RaisePropertyChanged(nameof(IdleHoursText));
        this.RaisePropertyChanged(nameof(IdleStreakText));
        this.RaisePropertyChanged(nameof(IdleActivitySummaryText));
    }

    /// <summary>True when the playing-state panel should be visible (track or world map present).</summary>
    public bool HasTrackOrAlbumWorld => HasTrack || IsAlbumWorldActive;

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
            {
                _spotifyHost.TrackStateChanged -= OnSpotifyStateChanged;
                _spotifyHost.PlaybackStopped -= OnSpotifyPlaybackStopped;
            }
            _spotifyHost = value;
            if (_spotifyHost is not null)
            {
                _spotifyHost.TrackStateChanged += OnSpotifyStateChanged;
                _spotifyHost.PlaybackStopped += OnSpotifyPlaybackStopped;
            }
        }
    }

    private void OnSpotifyStateChanged(object? sender, SpotifyTrackState state)
        => _ = ApplySpotifyStateAsync(state);

    /// <summary>Only meaningful for a queue-driven Spotify track (see <see cref="_queueDrivenSpotifyTrack"/>):
    /// Spotify has nothing left in its own queue to advance to, so Spectralis's own Queue takes over.
    /// A standalone "Play Spotify" session stopping naturally isn't Spectralis's concern.</summary>
    private void OnSpotifyPlaybackStopped(object? sender, EventArgs e)
    {
        if (!_queueDrivenSpotifyTrack)
        {
            return;
        }

        _spotifyState = null;
        _queueDrivenSpotifyTrack = false;
        _ = AutoAdvanceAsync();
    }

    private async Task ApplySpotifyStateAsync(SpotifyTrackState state)
    {
        // Late player_state_changed event from a pause issued by a since-completed Stop — see
        // _spotifyStopRequested. Applying it would resurrect the session Stop just cleared.
        if (_spotifyStopRequested)
        {
            return;
        }

        // A single-uri PlayUriAsync call gives Spotify no context to fall through to, so the SDK
        // never reports a distinct "ended"/no-track state the way OnSpotifyPlaybackStopped
        // expects — it just reports the same track paused at (or basically at) its own duration.
        // Catch that here too so queue-driven playback actually advances instead of sitting
        // paused at the end of every track.
        if (_queueDrivenSpotifyTrack && state.IsPaused && state.DurationMs > 0 &&
            state.PositionMs >= state.DurationMs - 1000 &&
            _spotifyState?.TrackId == state.TrackId)
        {
            _spotifyState = null;
            _queueDrivenSpotifyTrack = false;
            StopSpotifyLoopback();
            _ = AutoAdvanceAsync();
            return;
        }

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
            // Spotify (standalone "Play Spotify" and queue-driven mixed-source playlists alike)
            // never touches the local engine, so it was invisible to both scrobbling and Discord's
            // idle-activity stats — both are driven off Engine-only signals elsewhere. This event
            // is the same one Suno/BandLab/YouTube/SoundCloud already use to drive scrobbling
            // (see its doc comment), so Spotify plugs into the exact same downstream path.
            RemoteTrackLoaded?.Invoke(
                $"spotify:{state.TrackId}",
                state.Name,
                state.Artist,
                state.Album,
                state.DurationMs / 1000.0);

            _spotifyArtCts?.Cancel();
            var cts = _spotifyArtCts = new CancellationTokenSource();
            CoverArtBytes = state.AlbumArtUrl is not null
                ? await FetchSpotifyArtAsync(state.AlbumArtUrl, cts.Token)
                : null;
            if (!cts.IsCancellationRequested)
            {
                RefreshVisualizerOptions();
            }

            // Only meaningful for the standalone "Play Spotify" flow, where Spotify's own
            // server-side device queue genuinely is what's next. For a queue-driven playlist
            // (mixed local+Spotify or a synced Spotify playlist) Spectralis's own Queue is
            // authoritative instead — Spotify's device queue is empty/irrelevant since each
            // track is started with a single-uri play, no context — so calling this here
            // clobbered the correctly-built Queue panel with Spotify's (empty-ish, occasionally
            // just stale/duplicated) queue snapshot instead. SyncQueueCurrent just re-flags which
            // row is playing, using the list SyncQueueItems already built for this Queue.
            if (_queueDrivenSpotifyTrack)
            {
                SyncQueueCurrent();
            }
            else if (!cts.IsCancellationRequested && _spotifyHost is not null)
            {
                _ = RefreshSpotifyQueueAsync();
            }

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

        _queueDrivenSpotifyTrack = false;
        _spotifyStopRequested = false;
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

    public bool IsAlbumWorldActive => _pinnedAlbumWorldHtml is not null;
    public bool IsAlbumWorldShowingWorld => IsAlbumWorldActive && _albumWorldShowingWorld;
    internal string? AlbumWorldReadyJson { get; set; }
    internal string AlbumWorldCurrentTrackId => _albumWorldCurrentTrackId;
    internal string? AlbumWorldDir => _albumWorldDir;
    public Action<string, double>? AlbumPlayTrackDelegate { get; set; }
    public Action<double, bool>? AlbumWorldTick { get; set; }
    public Action? AlbumWorldExitDelegate { get; set; }
    public event Action<AlbumWorldTrackBridgeState>? AlbumWorldTrackChanged;
    public event Action<string, double>? AlbumWorldTrackCompleted;

    public void AttachAlbumWorld(EmbeddedHtmlContext worldHtml, string readyJson, string worldDir)
    {
        _pinnedAlbumWorldHtml = worldHtml;
        _albumWorldDir = worldDir;
        _albumWorldShowingWorld = true;
        AlbumWorldReadyJson = readyJson;
        EmbeddedHtml = worldHtml;
        if (_settings.EnableEmbeddedContent)
            UseEmbeddedHtmlSurface();
        RaiseSurfaceModeChanged();
    }

    public void DetachAlbumWorld()
    {
        _pinnedAlbumWorldHtml = null;
        _albumWorldDir = null;
        _albumWorldShowingWorld = false;
        AlbumWorldReadyJson = null;
        _albumWorldCurrentTrackId = string.Empty;
        if (_pickedInstalledHtml is not null && _settings.EnableEmbeddedContent)
        {
            EmbeddedHtml = _pickedInstalledHtml;
            ShowEmbeddedHtml = true;
        }
        else
        {
            ShowEmbeddedHtml = false;
            EmbeddedHtml = null;
        }
        RaiseSurfaceModeChanged();
    }

    public void BeginAlbumWorldTrackPlayback()
    {
        if (_pinnedAlbumWorldHtml is null)
            return;

        _albumWorldShowingWorld = false;
        RaiseSurfaceModeChanged();
    }

    public void NotifyAlbumWorldTrackChanged(AlbumWorldTrackBridgeState state)
    {
        _albumWorldCurrentTrackId = state.TrackId;
        AlbumWorldTrackChanged?.Invoke(state);
    }

    public void NotifyAlbumWorldTrackCompleted(string trackId, double playedSeconds)
    {
        if (string.Equals(_albumWorldCurrentTrackId, trackId, StringComparison.OrdinalIgnoreCase))
            _albumWorldCurrentTrackId = string.Empty;

        AlbumWorldTrackCompleted?.Invoke(trackId, playedSeconds);
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
            // A queued post-story surface means a story explainer is about to show first —
            // don't start audio underneath it. Playback begins later via TryAdvancePastStory's
            // resume handoff (OnEmbeddedResumeRequested), triggered by the story's own
            // spectral.resume() call once the reader finishes.
            if (startPlayback && trackInfo.EmbeddedHtmlAfterStory is null)
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
            RemoteLoadFailedRequested?.Invoke(input, ex.Message);
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
        ResetPositionDisplay();
        try
        {
            // WebView widget fallback: embed the platform player directly (SoundCloud, Suno, Spotify).
            if (resolved.IsWebViewFallback())
            {
                StopSpotifyPlayback();
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
            {
                _ = _engine.PrepareNextAsync(nextQueuePath);
                UpcomingQueueTrackReady?.Invoke(nextQueuePath);
            }

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
            RemoteLoadFailedRequested?.Invoke(resolved.SourceUrl, ex.Message);
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
        ResetPositionDisplay();
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

            if (startPlayback && !_engine.IsPlaying && await ShouldPlayWithContentWarningAsync(path))
            {
                _engine.Play();
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
            {
                _ = _engine.PrepareNextAsync(nextPath);
                UpcomingQueueTrackReady?.Invoke(nextPath);
            }
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

        // Position must be pushed to the slider before length. The transport slider's
        // Value is TwoWay-bound and its Maximum is bound to LengthSeconds — if Maximum
        // shrinks (e.g. a new Spotify track is shorter than where the previous one left
        // off) while Value still holds the old track's near-end position, the Slider
        // clamps Value down to the new Maximum itself, and that clamp round-trips back
        // through the TwoWay binding as a real seek. On a Spotify track transition that
        // lands the seek right at the new track's own end, which immediately skips it.
        // Updating position first keeps Value small before Maximum ever moves, so the
        // Slider never needs to coerce it.
        if (Math.Abs(position - _positionSeconds) > 0.05)
        {
            _positionSeconds = Math.Min(position, length);
            this.RaisePropertyChanged(nameof(PositionSeconds));
            this.RaisePropertyChanged(nameof(PositionText));
        }

        LengthSeconds = length;
        this.RaisePropertyChanged(nameof(LengthText));

        if (IsAlbumWorldActive)
        {
            AlbumWorldTick?.Invoke(position, IsPlaying);
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

    /// <summary>
    /// Zeroes the displayed position/length immediately when a new load begins, before the engine
    /// actually has a stream open. Without this, the slider keeps showing the previous track's
    /// end-of-song position for the whole async window (metadata read, or a full remote download
    /// for SoundCloud/YouTube/etc.) until the first post-load <see cref="RefreshFromEngine"/> tick.
    /// </summary>
    private void ResetPositionDisplay()
    {
        _positionSeconds = 0;
        this.RaisePropertyChanged(nameof(PositionSeconds));
        this.RaisePropertyChanged(nameof(PositionText));
        LengthSeconds = 0;
        this.RaisePropertyChanged(nameof(LengthText));
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

    /// <summary>Pauses Spotify's real device (if it was the active source) and clears the
    /// Spotify-specific bookkeeping tied to it. Doesn't touch Title/Artist/HasTrack — callers
    /// replacing the whole "now playing" surface should follow up with ApplyTrack themselves.
    /// This used to be duplicated (slightly differently) between ApplyTrack and
    /// ResetPlaybackSession, which is how Spotify ended up able to keep playing in the background
    /// after switching to a local track: ApplyTrack cleared _spotifyState without ever actually
    /// telling the device to stop, so by the time Stop was pressed there was nothing left for
    /// ResetPlaybackSession's own (now-removed) _spotifyState check to catch.</summary>
    private void StopSpotifyPlayback()
    {
        if (_spotifyState is null)
        {
            return;
        }

        _spotifyStopRequested = true;
        _ = _spotifyHost?.StopAsync();
        _spotifyState = null;
        _queueDrivenSpotifyTrack = false;
        StopSpotifyLoopback();
        _spotifyVisualizer = null;
        _engine.ExternalVisualizerSource = null;
    }

    private void ApplyTrack(TrackInfo? track)
    {
        var wasSpotify = _spotifyState is not null;
        StopSpotifyPlayback();
        _spotifyArtCts?.Cancel();
        _spotifyLyricsCts?.Cancel();
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
            RefreshVisualizerOptions();
            return;
        }

        HasTrack = true;
        Title = track.DisplayTitle;
        Artist = track.Artist;
        Album = track.Album;
        CoverArtBytes = track.CoverArt;
        ApplyEmbeddedModules(track);
        Notepads.LoadEmbeddedNotepadsForTrack(track.SourcePath);

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
        RefreshVisualizerOptions();
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
        _trackHasEmbeddedSurface = track.EmbeddedHtml is not null ||
            track.EmbeddedVisualizer is not null ||
            track.EmbeddedMarkdown is not null ||
            track.EmbeddedVideo is not null;

        // When a world map is pinned, keep it live — only update the non-HTML modules.
        if (_pinnedAlbumWorldHtml is not null && _albumWorldShowingWorld)
        {
            this.RaiseAndSetIfChanged(ref _embeddedVisualizer, track.EmbeddedVisualizer);
            this.RaisePropertyChanged(nameof(HasEmbeddedVisualizer));
            this.RaisePropertyChanged(nameof(HasEmbeddedModules));
            return;
        }

        this.RaiseAndSetIfChanged(ref _embeddedVisualizer, track.EmbeddedVisualizer);
        this.RaiseAndSetIfChanged(ref _embeddedMarkdown, track.EmbeddedMarkdown);
        this.RaiseAndSetIfChanged(ref _embeddedVideo, track.EmbeddedVideo);
        _embeddedHtmlAfterStory = track.EmbeddedHtmlAfterStory;
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
        _trackHasEmbeddedSurface = false;

        if (_pinnedAlbumWorldHtml is not null && _albumWorldShowingWorld && _settings.EnableEmbeddedContent)
        {
            // Album world stays live across track changes.
            EmbeddedHtml = _pinnedAlbumWorldHtml;
            ShowEmbeddedHtml = true;
        }
        else if (_pickedInstalledHtml is not null && _settings.EnableEmbeddedContent)
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
        // HasTrack/IsPlaying instead of _engine.IsLoaded/_engine.IsPlaying: Spotify playback
        // never loads anything into the local engine (it plays through the Spotify SDK), so
        // the engine-only checks were false for the entire duration of Spotify playback and
        // auto-cycle silently never fired. HasTrack/IsPlaying already track both local and
        // Spotify playback correctly (see ApplyTrack and the Spotify state-change handler).
        //
        // ShowVisualizerControls (not ShowVisualizer) + IsVisualizerLocked (not IsSurfaceEmbedded):
        // redeemed/"Special" visualizers render through the same embedded-HTML surface as real
        // capsules (ShowEmbeddedHtml=true, ShowVisualizer=false), so gating on ShowVisualizer/
        // IsSurfaceEmbedded froze auto-cycle solid the moment it landed on one. IsVisualizerLocked
        // is the guard that actually means "a capsule/album-world surface owns the screen" and is
        // untouched by picking a redeemed visualizer, so it's the right thing to stop cycling for.
        if (!AutoCycleVisualizers ||
            !ShowVisualizerControls ||
            !HasTrack ||
            !IsPlaying ||
            IsVisualizerLocked ||
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
        _idleActivityTick?.Dispose();
        _remoteLoadCts?.Cancel();
        _remoteLoadCts?.Dispose();
        RemoteAudioCache.TryDelete(_remoteAudioTempPath);
    }
}
