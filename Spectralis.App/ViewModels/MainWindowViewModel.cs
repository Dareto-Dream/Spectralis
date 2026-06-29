using System.Collections.ObjectModel;
using ReactiveUI;
using Spectralis.App.Design;
using Spectralis.App.Services;
using Spectralis.Core.Audio;
using Spectralis.Core.Audio.Effects;
using Spectralis.Core.Capsule;
using Spectralis.Core.Common;
using Spectralis.Core.Metadata;
using Spectralis.Core.Scrobbling;

namespace Spectralis.App.ViewModels;

/// <summary>A sidebar destination: label plus the ViewModel it routes to.</summary>
public sealed class NavSection : ViewModelBase
{
    public NavSection(string label, string iconData, ViewModelBase content)
    {
        Label = label;
        IconData = iconData;
        Content = content;
    }

    public string Label { get; }

    public string IconData { get; }

    public ViewModelBase Content { get; }
}

public sealed class MainWindowViewModel : ViewModelBase
{
    private NavSection _selectedSection;
    private bool _isSidebarCollapsed;
    private bool _isP2wModeActive;
    private bool _clipboardToastVisible;
    private string _clipboardToastTitle = string.Empty;
    private string _clipboardToastSubtitle = string.Empty;
    private string? _pendingClipboardUrl;
    private RemoteAudioResolveResult? _pendingClipboardResolved;
    private byte[]? _clipboardToastArtwork;
    private DateTimeOffset _clipboardToastShownAt;
    private ListeningActivitySnapshot _idleActivity = ListeningActivitySnapshot.Empty;
    private string _mostRecentSongSource = string.Empty;
    private string _mostRecentSongLabel = string.Empty;

    public MainWindowViewModel()
    {
        AppSettings = AppSettingsStore.Load();
        AppThemeService.Apply(AppSettings);
        _isSidebarCollapsed = AppSettings.SidebarCollapsed;
        Engine = new AudioEngine();
        EffectChain = new EffectChain();
        Engine.SetEffectChain(EffectChain);
        EffectChain.Changed += (_, _) => Engine.RebuildEffectChain();
        NowPlaying = new NowPlayingViewModel(Engine, AppSettings);

        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spectralis", "library-avalonia.db");
        LibraryDatabase = new LibraryDatabase(databasePath);
        Library = new LibraryViewModel(
            LibraryDatabase,
            new LibraryScanner(LibraryDatabase),
            PlayFromLibraryAsync,
            AppSettings);
        Library.InitializeWatchedFolders(AppSettings.LibraryAutoScanOnOpen);
        Playlists = new PlaylistsViewModel(LibraryDatabase, PlayFromLibraryAsync);
        Scrobbling = new ScrobblingService(() => new ScrobblingConfig(
            AppSettings.LastFmEnabled,
            AppSettings.LastFmApiKey,
            AppSettings.LastFmApiSecret,
            AppSettings.LastFmSessionKey,
            AppSettings.ListenBrainzEnabled,
            AppSettings.ListenBrainzToken));
        _ = Scrobbling.DrainQueueAsync();
        _scrobbleTick = Avalonia.Threading.DispatcherTimer.Run(
            () =>
            {
                Scrobbling.Tick(Engine.GetPosition(), Engine.IsPlaying);
                return true;
            },
            TimeSpan.FromSeconds(5));
        RefreshIdleActivity();
        _idleActivityTick = Avalonia.Threading.DispatcherTimer.Run(
            () =>
            {
                RefreshIdleActivity();
                return true;
            },
            TimeSpan.FromSeconds(30));

        NowPlaying.LocalTrackLoaded += path =>
        {
            LibraryDatabase.IncrementPlayCount(path);
            ApplyBeatGridForTrack(path);
            if (Engine.CurrentTrack is { } track)
            {
                RememberMostRecentSong(path, track.DisplayTitle, track.Artist);
                Scrobbling.NotifyTrackLoaded(
                    path,
                    track.DisplayTitle,
                    track.Artist,
                    track.Album,
                    track.Duration.TotalSeconds);
            }
        };

        NowPlaying.RemoteTrackLoaded += (sourceUrl, title, artist, album, durationSeconds) =>
        {
            RememberMostRecentSong(sourceUrl, title, artist);
            Scrobbling.NotifyTrackLoaded(sourceUrl, title, artist, album, durationSeconds);
        };

        Library.TrackAnalyzed += (_, result) =>
        {
            // Analysis finishing for the playing track lights up its beat grid live.
            if (string.Equals(Engine.CurrentTrack?.SourcePath, result.Path, StringComparison.OrdinalIgnoreCase))
            {
                NowPlaying.SetBeatGrid(result.Bpm, result.FirstBeatOffset.TotalSeconds);
            }
        };
        if (AppSettings.AutoAnalyzeBpm && LibraryDatabase.Count() > 0)
        {
            Library.StartAnalysis();
        }
        SharedPlay = new SharedPlayViewModel();
        SharedPlay.ApplySettings(AppSettings);
        RandomizerTools = new RandomizerToolsViewModel();
        StreamerQueue = new StreamerQueueViewModel();
        StreamerQueue.ApplySettings(AppSettings);
        NowPlaying.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(NowPlayingViewModel.PositionSeconds) or
                nameof(NowPlayingViewModel.IsPlaying))
            {
                SharedPlay.NotifyPlayback(
                    Engine.CurrentTrack,
                    NowPlaying.IsPlaying,
                    NowPlaying.PositionSeconds,
                    NowPlaying.LengthSeconds,
                    "tick");
            }
        };
        Capsules = new CapsulesViewModel(
            (path, track, startPlayback) => NowPlaying.LoadPreparedTrackAsync(
                path,
                track,
                startPlayback,
                ownsTemporaryFile: true));
        Capsules.AlbumPlaybackPause  = () => Engine.Pause();
        Capsules.AlbumPlaybackResume = () => Engine.Play();
        Capsules.AlbumPlaybackSeek   = pos => Engine.Seek((float)pos);
        NowPlaying.SessionReset += (_, _) => Capsules.Clear();
        NowPlaying.LyricsTargetActivated += (_, _) => SelectSection(NowPlaying);
        TimingStudio = new TimingStudioViewModel(Engine);
        ObsOverlay = new ObsOverlayCoordinator(Engine, NowPlaying, AppSettings);
        ObsOverlay.Start();
        DiscordPresence = new DiscordPresenceCoordinator(Engine, () => IdleActivity);
        DiscordPresence.SetEnabled(AppSettings.EnableDiscordRichPresence);
        ObsEditor = new ObsEditorViewModel(
            AppSettings,
            enabled =>
            {
                ObsOverlay.SetEnabled(enabled);
                RefreshObsSettings();
            },
            () =>
            {
                ObsOverlay.Restart();
                RefreshObsSettings();
            },
            layout => ObsOverlay.SetLayout(layout),
            (id, layout) => ObsOverlay.SetNamedLayout(id, layout),
            id => ObsOverlay.RemoveNamedLayout(id));
        StreamerSettings = new StreamerSettingsViewModel(AppSettings, ObsEditor);
        Settings = new SettingsViewModel(
            AppSettings,
            NowPlaying,
            enabled => DiscordPresence.SetEnabled(enabled),
            ObsEditor,
            Library,
            StreamerSettings);

        Sections = new ObservableCollection<NavSection>
        {
            new("Now Playing", IconData.NowPlaying, NowPlaying),
            new("Library", IconData.Library, Library),
            new("Playlists", IconData.Playlists, Playlists),
            new("Capsules", IconData.Capsules, Capsules),
            new("Shared Play", IconData.SharedPlay, SharedPlay),
            new("Streamer Queue", IconData.StreamerQueue, StreamerQueue),
            new("Randomizer", IconData.Randomizer, RandomizerTools),
            new("Timing Studio", IconData.TimingStudio, TimingStudio),
            new("OBS Overlay", IconData.Obs, ObsEditor),
            new("Settings", IconData.Settings, Settings),
        };

        _selectedSection = Sections[0];

        RefreshObsSettings();

        NowPlaying.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(NowPlayingViewModel.HasTrack):
                case nameof(NowPlayingViewModel.Title):
                case nameof(NowPlayingViewModel.Artist):
                    this.RaisePropertyChanged(nameof(WindowTitle));
                    this.RaisePropertyChanged(nameof(StatusText));
                    this.RaisePropertyChanged(nameof(StatusHintText));
                    this.RaisePropertyChanged(nameof(IsStatusAccent));
                    break;

                case nameof(NowPlayingViewModel.IsPlaying):
                case nameof(NowPlayingViewModel.RemoteStatus):
                case nameof(NowPlayingViewModel.SourceLabel):
                case nameof(NowPlayingViewModel.HasQueueItems):
                case nameof(NowPlayingViewModel.QueueUpcomingText):
                    this.RaisePropertyChanged(nameof(StatusText));
                    this.RaisePropertyChanged(nameof(StatusHintText));
                    this.RaisePropertyChanged(nameof(IsStatusAccent));
                    break;

                case nameof(NowPlayingViewModel.OutputRateText):
                    this.RaisePropertyChanged(nameof(OutputStatusText));
                    break;
            }
        };
    }

    /// <summary>"Artist - Track - Spectralis" while a track is active, legacy-style.</summary>
    public string WindowTitle
    {
        get
        {
            if (!NowPlaying.HasTrack)
            {
                return "Spectralis";
            }

            return string.IsNullOrWhiteSpace(NowPlaying.Artist)
                ? $"{NowPlaying.Title} - Spectralis"
                : $"{NowPlaying.Artist} - {NowPlaying.Title} - Spectralis";
        }
    }

    public string StatusText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(NowPlaying.RemoteStatus) && !NowPlaying.HasTrack)
            {
                return NowPlaying.RemoteStatus;
            }

            if (!NowPlaying.HasTrack)
            {
                return "Ready";
            }

            var track = string.IsNullOrWhiteSpace(NowPlaying.Artist)
                ? NowPlaying.Title
                : $"{NowPlaying.Artist} - {NowPlaying.Title}";
            var status = NowPlaying.IsPlaying ? $"Playing  {track}" : $"Paused  {track}";
            if (NowPlaying.Queue.Count > 1 && NowPlaying.Queue.CurrentIndex >= 0)
            {
                status += $"  ·  {NowPlaying.Queue.CurrentIndex + 1} of {NowPlaying.Queue.Count}";
            }

            if (!string.IsNullOrWhiteSpace(NowPlaying.SourceLabel))
            {
                status += $"  ·  via {NowPlaying.SourceLabel}";
            }

            return status;
        }
    }

    public bool IsStatusAccent => NowPlaying.IsPlaying;

    public string OutputStatusText => $"Output: {NowPlaying.OutputRateText}";

    private static string KeyboardShortcutText =>
        "Space: Play/Pause  ·  ←→: Seek  ·  ↑↓: Volume  ·  M: Mute  ·  Ctrl+←→: Prev/Next  ·  Ctrl+O: Open  ·  Ctrl+,: Settings";

    public string StatusHintText => NowPlaying.HasTrack
        ? KeyboardShortcutText
        : IdleActivityText;

    public string IdleActivityText => BuildIdleActivityText(IdleActivity);

    public string VersionText => $"v{DiagnosticsSnapshot.CurrentVersion}";

    public ObsOverlayCoordinator ObsOverlay { get; }

    public ObsEditorViewModel ObsEditor { get; }

    public StreamerSettingsViewModel StreamerSettings { get; }

    public DiscordPresenceCoordinator DiscordPresence { get; }

    public AudioEngine Engine { get; }

    public ScrobblingService Scrobbling { get; }

    private readonly IDisposable _scrobbleTick;
    private readonly IDisposable _idleActivityTick;

    public ListeningActivitySnapshot IdleActivity => _idleActivity;

    public bool CanPlayMostRecentSong => !string.IsNullOrWhiteSpace(_mostRecentSongSource);

    public string MostRecentSongLabel => _mostRecentSongLabel;

    public EffectChain EffectChain { get; }

    public AppSettings AppSettings { get; }

    public LibraryDatabase LibraryDatabase { get; }

    public NowPlayingViewModel NowPlaying { get; }
    public LibraryViewModel Library { get; }
    public PlaylistsViewModel Playlists { get; }
    public SharedPlayViewModel SharedPlay { get; }
    public StreamerQueueViewModel StreamerQueue { get; }
    public RandomizerToolsViewModel RandomizerTools { get; }
    public CapsulesViewModel Capsules { get; }
    public TimingStudioViewModel TimingStudio { get; }
    public SettingsViewModel Settings { get; }

    public ObservableCollection<NavSection> Sections { get; }

    public NavSection SelectedSection
    {
        get => _selectedSection;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSection, value);
            this.RaisePropertyChanged(nameof(CurrentContent));
            this.RaisePropertyChanged(nameof(IsNowPlayingSelected));
            this.RaisePropertyChanged(nameof(IsLibrarySelected));
            this.RaisePropertyChanged(nameof(IsPlaylistsSelected));
        }
    }

    public ViewModelBase CurrentContent => SelectedSection.Content;

    public bool IsNowPlayingSelected => ReferenceEquals(SelectedSection.Content, NowPlaying);
    public bool IsLibrarySelected => ReferenceEquals(SelectedSection.Content, Library);
    public bool IsPlaylistsSelected => ReferenceEquals(SelectedSection.Content, Playlists);

    /// <summary>Routes the shell to the section hosting <paramref name="content"/>.</summary>
    public void SelectSection(ViewModelBase content)
    {
        var section = Sections.FirstOrDefault(section => ReferenceEquals(section.Content, content));
        if (section is not null)
        {
            SelectedSection = section;
        }
    }

    public bool IsP2wModeActive
    {
        get => _isP2wModeActive;
        set => this.RaiseAndSetIfChanged(ref _isP2wModeActive, value);
    }

    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        set
        {
            if (_isSidebarCollapsed == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _isSidebarCollapsed, value);
            this.RaisePropertyChanged(nameof(SidebarWidth));
            this.RaisePropertyChanged(nameof(SidebarToggleIconData));
            this.RaisePropertyChanged(nameof(SidebarToggleTip));
            AppSettings.SidebarCollapsed = value;
            AppSettingsStore.Save(AppSettings);
        }
    }

    public double SidebarWidth => IsSidebarCollapsed ? 68 : 208;

    public string SidebarToggleIconData => IsSidebarCollapsed
        ? IconData.SidebarExpand
        : IconData.SidebarCollapse;

    public string SidebarToggleTip => IsSidebarCollapsed
        ? "Expand sidebar"
        : "Collapse sidebar";

    public bool ClipboardToastVisible
    {
        get => _clipboardToastVisible;
        private set => this.RaiseAndSetIfChanged(ref _clipboardToastVisible, value);
    }

    public string ClipboardToastTitle
    {
        get => _clipboardToastTitle;
        private set => this.RaiseAndSetIfChanged(ref _clipboardToastTitle, value);
    }

    public string ClipboardToastSubtitle
    {
        get => _clipboardToastSubtitle;
        private set => this.RaiseAndSetIfChanged(ref _clipboardToastSubtitle, value);
    }

    public byte[]? ClipboardToastArtwork
    {
        get => _clipboardToastArtwork;
        private set => this.RaiseAndSetIfChanged(ref _clipboardToastArtwork, value);
    }

    /// <summary>
    /// Shows the clipboard toast for a link that has already been validated (quick
    /// metadata-only resolve — see MainWindow's clipboard monitor). <paramref name="resolved"/>
    /// is reused on Play when it's already fully playback-ready (MetadataOnly == false);
    /// otherwise Play falls back to a full resolve.
    /// </summary>
    public void ShowClipboardUrlToast(string url, RemoteAudioResolveResult resolved, byte[]? artwork)
    {
        _pendingClipboardUrl = url;
        _pendingClipboardResolved = resolved;
        ClipboardToastTitle = "Copied media URL";
        ClipboardToastSubtitle = !string.IsNullOrWhiteSpace(resolved.Title)
            ? FormatResolvedClipboardSubtitle(resolved.Title, resolved.Artist)
            : BuildClipboardToastSubtitle(url);
        ClipboardToastArtwork = artwork;
        _clipboardToastShownAt = DateTimeOffset.UtcNow;
        ClipboardToastVisible = true;
    }

    public void DismissClipboardToast()
    {
        _pendingClipboardUrl = null;
        _pendingClipboardResolved = null;
        ClipboardToastArtwork = null;
        ClipboardToastVisible = false;
    }

    public void DismissClipboardToastIfExpired(TimeSpan lifetime)
    {
        if (ClipboardToastVisible && DateTimeOffset.UtcNow - _clipboardToastShownAt > lifetime)
        {
            DismissClipboardToast();
        }
    }

    public async Task PlayClipboardToastUrlAsync()
    {
        var url = _pendingClipboardUrl;
        var resolved = _pendingClipboardResolved;
        DismissClipboardToast();
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        SelectedSection = Sections[0];
        if (resolved is { MetadataOnly: false })
        {
            await NowPlaying.LoadPreResolvedUrlAsync(resolved);
            return;
        }
        await NowPlaying.LoadUrlAsync(url);
    }

    public async Task PlayMostRecentSongAsync()
    {
        var source = _mostRecentSongSource;
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        SelectSection(NowPlaying);
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            await NowPlaying.LoadUrlAsync(source);
            return;
        }

        if (File.Exists(source))
        {
            await NowPlaying.PlayQueueAsync([source], 0);
        }
    }

    /// <summary>
    /// Entry point for drops and OS file-open: files play, folders scan into the library.
    /// </summary>
    public async Task OpenFilesAsync(IEnumerable<string> paths)
    {
        var pathList = paths.ToList();

        var folders = pathList.Where(Directory.Exists).ToList();
        if (folders.Count > 0)
        {
            SelectedSection = Sections.First(section => section.Content == Library);
            _ = Library.ScanPathsAsync(folders);
        }

        var files = pathList
            .Where(path => File.Exists(path) && SupportedAudioFormats.IsSupportedExtension(path))
            .ToList();

        var capsuleFiles = pathList
            .Where(path => File.Exists(path) && CapsulesViewModel.IsPackage(path))
            .ToList();
        if (capsuleFiles.Count > 0)
        {
            var hasAlbumWorld = capsuleFiles.Any(p =>
                Path.GetExtension(p).Equals(".spectral", StringComparison.OrdinalIgnoreCase));
            if (!hasAlbumWorld)
            {
                SelectSection(NowPlaying);
            }
            await Capsules.OpenFilesAsync(capsuleFiles, AppSettings.AutoPlayOnOpen);
            if (hasAlbumWorld)
            {
                SelectSection(Capsules);
            }
        }

        if (files.Count > 0)
        {
            SelectedSection = Sections[0];
            if (AppSettings.QueueByDefault)
            {
                await NowPlaying.QueueFilesAsync(files, AppSettings.AutoPlayOnOpen);
            }
            else
            {
                await NowPlaying.PlayQueueAsync(files, 0, AppSettings.AutoPlayOnOpen);
            }
        }
    }

    /// <summary>External (second-instance/protocol) opens with an explicit queue intent.</summary>
    public async Task QueueExternalFilesAsync(IReadOnlyList<string> paths, Core.Platform.ExternalOpenIntent intent)
    {
        var files = paths
            .Where(path => File.Exists(path) && SupportedAudioFormats.IsSupportedExtension(path))
            .ToList();
        if (files.Count == 0)
        {
            return;
        }

        SelectSection(NowPlaying);
        if (intent == Core.Platform.ExternalOpenIntent.QueueNext)
        {
            await NowPlaying.QueueFilesNextAsync(files);
        }
        else
        {
            await NowPlaying.QueueFilesAsync(files, AppSettings.AutoPlayOnOpen);
        }
    }

    /// <summary>Shows the stored beat grid for a loaded track, or analyzes it on demand.</summary>
    private void ApplyBeatGridForTrack(string path)
    {
        var entry = LibraryDatabase.GetAllEntries()
            .FirstOrDefault(e => string.Equals(e.Track.SourcePath, path, StringComparison.OrdinalIgnoreCase));
        if (entry?.Track.Bpm is { } bpm)
        {
            NowPlaying.SetBeatGrid(bpm, 0);
            return;
        }

        // Unanalyzed: analyze just this file in the background (legacy behavior).
        if (entry is not null && AppSettings.AutoAnalyzeBpm)
        {
            _ = Library.AnalyzePathsAsync([path]);
        }
    }

    private async Task PlayFromLibraryAsync(IReadOnlyList<string> paths, int startIndex)
    {
        SelectedSection = Sections[0];
        await NowPlaying.PlayQueueAsync(paths, startIndex);
    }

    private void RefreshObsSettings()
    {
        ObsEditor.ObsOverlayUrl = ObsOverlay.OverlayUrl ?? string.Empty;
        ObsEditor.ObsStatus = ObsOverlay.IsRunning
            ? $"Overlay server running on port {AppSettings.ObsOverlayPort}"
            : AppSettings.EnableObsOverlay
                ? $"Overlay server unavailable: {ObsOverlay.StartupError}"
                : "Overlay server disabled.";
    }

    private void RefreshIdleActivity()
    {
        var next = ListeningActivitySnapshot.FromHistory(ScrobbleQueue.LoadHistory());
        if (next == _idleActivity)
        {
            return;
        }

        _idleActivity = next;
        this.RaisePropertyChanged(nameof(IdleActivity));
        this.RaisePropertyChanged(nameof(IdleActivityText));
        this.RaisePropertyChanged(nameof(StatusHintText));
    }

    private void RememberMostRecentSong(string source, string title, string artist)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        _mostRecentSongSource = source;
        _mostRecentSongLabel = string.IsNullOrWhiteSpace(title)
            ? source
            : string.IsNullOrWhiteSpace(artist)
                ? title
                : $"{artist} - {title}";
        this.RaisePropertyChanged(nameof(CanPlayMostRecentSong));
        this.RaisePropertyChanged(nameof(MostRecentSongLabel));
    }

    private static string BuildIdleActivityText(ListeningActivitySnapshot snapshot)
    {
        if (!snapshot.HasHistory)
        {
            return "Idle activity: no local listens yet";
        }

        var hours = snapshot.TotalHours >= 10
            ? snapshot.TotalHours.ToString("0")
            : snapshot.TotalHours.ToString("0.#");
        var favorite = string.IsNullOrWhiteSpace(snapshot.TopTrackDisplay)
            ? ""
            : $" | favorite {snapshot.TopTrackDisplay} ({snapshot.TopTrackPlays} plays)";
        var artist = string.IsNullOrWhiteSpace(snapshot.TopArtist)
            ? ""
            : $" | top artist {snapshot.TopArtist} ({snapshot.TopArtistPlays} plays)";
        var streak = snapshot.CurrentStreakDays > 1
            ? $" | {snapshot.CurrentStreakDays} day streak"
            : "";
        return $"Idle activity: {snapshot.TotalScrobbles:N0} listens | {hours}h{favorite}{artist}{streak}";
    }

    private static string FormatResolvedClipboardSubtitle(string? title, string? artist) =>
        string.IsNullOrWhiteSpace(artist) ? title! : $"{artist} — {title}";

    private static string BuildClipboardToastSubtitle(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var host = uri.Host.ToLowerInvariant();
        var serviceLabel = host switch
        {
            _ when host.Contains("youtube.com") || host.Contains("youtu.be") => "YouTube",
            _ when host.Contains("soundcloud.com") || host.Contains("snd.sc") => "SoundCloud",
            _ when host.Contains("suno.com") || host.Contains("suno.ai") => "Suno",
            _ when host.Contains("bandlab.com") => "BandLab",
            _ when host.Contains("untitled.stream") => "Untitled",
            _ when host.Contains("spotify.com") || host.Contains("spotify.link") => "Spotify",
            _ => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host,
        };
        return $"{serviceLabel} — click Play to open";
    }
}
