using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Spectralis.App.Controls;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.Core.Common;
using Spectralis.Core.Embedded;
using Spectralis.Core.Integrations.Web;
using Spectralis.Core.Layout;
using Spectralis.Core.Platform;

namespace Spectralis.App.Views;

public partial class NowPlayingView : Grid
{
    private NowPlayingViewModel? _viewModel;
    private LyricsInspectorWindow? _lyricsInspectorWindow;
    private WebViewControl.WebView? _youTubeWebView;
    private YouTubeVideoPageServer? _youTubeVideoServer;
    private DispatcherTimer? _youTubeSyncTimer;
    private string _loadedYouTubeVideoId = string.Empty;
    private Control? _embeddedControl;
    private IWebViewHost? _embeddedHost;
    private WebViewHostService? _embeddedService;
    // Persistent WebView2 host for capsule HTML (Windows only). Created once and reused
    // across track changes so WebView2 never needs to re-initialize (~400ms) and the HWND
    // is never destroyed while CreateCoreWebView2ControllerAsync is still in flight.
#if WINDOWS
    private WebView2Host? _persistentWv2;
#endif
    private DispatcherTimer? _embeddedFramePushTimer;
    private DispatcherTimer? _resizeSettleTimer;
    private string _loadedEmbeddedHtmlId = string.Empty;
    private double _lastPushedTime = double.MinValue;
    private bool _lastPushedActive;
    private volatile bool _embeddedExecPending;
    // Shared frame cache: timer writes on UI thread, CefGlue bridge reads on IPC thread.
    // Volatile ensures memory visibility; string reference reads/writes are atomic.
    private volatile string _latestFrameJson = string.Empty;

    // Frame-push diagnostics
    private static readonly string _webviewPerfLog = AppLogPaths.For("webview-perf.log");
    private readonly Stopwatch _pushClock = new();
    private long _pushCount;
    private long _skipCount;
    private long _lateCount;
    private double _maxIntervalMs;
    private double _sumIntervalMs;
    private long _intervalSamples;
    private long _lastStatsTick;

public NowPlayingView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.AlbumWorldTrackChanged -= OnAlbumWorldTrackChanged;
                _viewModel.AlbumWorldTrackCompleted -= OnAlbumWorldTrackCompleted;
                _viewModel.Notepads.PopOutRequested -= OnNotepadPopOutRequested;
            }

            _viewModel = DataContext as NowPlayingViewModel;
            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                _viewModel.AlbumWorldTrackChanged += OnAlbumWorldTrackChanged;
                _viewModel.AlbumWorldTrackCompleted += OnAlbumWorldTrackCompleted;
                _viewModel.Notepads.PopOutRequested += OnNotepadPopOutRequested;
                ApplyYouTubeVideoMode();
                ApplyEmbeddedHtmlMode();
                ApplyDeadZoneLayout();
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _resizeSettleTimer?.Stop();
            StopYouTubeVideoMode();
            StopEmbeddedHtmlMode();
#if WINDOWS
            _persistentWv2?.Dispose();
            _persistentWv2 = null;
#endif
        };
        SizeChanged += (_, _) =>
        {
            ApplyDeadZoneLayout();
            ScheduleEmbeddedSurfaceResizeNudge();
        };
        // The embedded HTML surface lives in the player column, which grows and shrinks as
        // the docked sidebars (queue, lyrics, song wars, notepad, ...) toggle. That inner
        // column resize never reaches this view's own SizeChanged, so the CEF OSR browser
        // would keep painting at whatever size it had when the last panel opened. Watch the
        // host container directly and let the debounced nudge settle it.
        EmbeddedHtmlHost.SizeChanged += (_, _) => ScheduleEmbeddedSurfaceResizeNudge();
        VisualizerNameLabel.SizeChanged += (_, _) => ApplyVisualizerLabelDeadZoneAvoidance();
        LyricsSidebarBorder.SizeChanged += (_, _) => ApplySidebarDeadZoneAvoidance();
        QueueSidebarBorder.SizeChanged += (_, _) => ApplySidebarDeadZoneAvoidance();
        SongWarsSidebarBorder.SizeChanged += (_, _) => ApplySidebarDeadZoneAvoidance();
        NotepadSidebarBorder.SizeChanged += (_, _) => ApplySidebarDeadZoneAvoidance();
    }

    // ── Dead zones (app-wide) ────────────────────────────────────────────────

    /// <summary>Recomputes every dead-zone-aware layout adjustment in this view (visualizer panel,
    /// its name label, and the docked sidebars). Panel-level shift runs before the label so the
    /// label repositions relative to the visualizer's already-adjusted area.</summary>
    private void ApplyDeadZoneLayout()
    {
        ApplyVisualizerPanelDeadZoneAvoidance();
        ApplyVisualizerLabelDeadZoneAvoidance();
        ApplySidebarDeadZoneAvoidance();
    }

    /// <summary>Debounced trigger for <see cref="IWebViewHost.NudgeResize"/> on the active HTML
    /// visualizer surface. A manual drag-resize fires many SizeChanged events, each rescheduling
    /// this timer, so the nudge only actually runs once the size has settled — including the
    /// single instant jump from a window maximize/restore, which is the case that otherwise
    /// leaves the embedded browser painting at its pre-resize size (see NudgeResize doc).</summary>
    private void ScheduleEmbeddedSurfaceResizeNudge()
    {
        if (_embeddedHost is null)
        {
            return;
        }

        if (_resizeSettleTimer is null)
        {
            _resizeSettleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _resizeSettleTimer.Tick += (_, _) =>
            {
                _resizeSettleTimer!.Stop();
                _embeddedHost?.NudgeResize();
            };
        }

        _resizeSettleTimer.Stop();
        _resizeSettleTimer.Start();
    }

    /// <summary>
    /// Shifts/shrinks the visualizer surface away from any dead zone overlapping it, by insetting
    /// from whichever single edge most cheaply clears each zone (same "cheapest direction" idea as
    /// <see cref="DeadZoneHelper"/>'s widget push-away, adapted for a panel that fills its container
    /// rather than a small movable widget).
    /// </summary>
    private void ApplyVisualizerPanelDeadZoneAvoidance()
    {
        if (_viewModel is null) return;
        if (VisualizerSurfacePanel.Parent is not Control parent) return;
        double w = parent.Bounds.Width, h = parent.Bounds.Height;
        var zones = _viewModel.DeadZones;

        if (w <= 0 || h <= 0 || zones.Count == 0)
        {
            VisualizerSurfacePanel.Margin = default;
            return;
        }

        double left = 0, top = 0, right = 0, bottom = 0;
        foreach (var dz in zones)
        {
            double needLeft = dz.X + dz.W;
            double needRight = 1 - dz.X;
            double needTop = dz.Y + dz.H;
            double needBottom = 1 - dz.Y;
            double min = Math.Min(Math.Min(needLeft, needRight), Math.Min(needTop, needBottom));

            if (min == needLeft) left = Math.Max(left, needLeft);
            else if (min == needRight) right = Math.Max(right, needRight);
            else if (min == needTop) top = Math.Max(top, needTop);
            else bottom = Math.Max(bottom, needBottom);
        }

        VisualizerSurfacePanel.Margin = new Thickness(left * w, top * h, right * w, bottom * h);
    }

    /// <summary>
    /// Docked sidebars (Lyrics/Queue/Song Wars/Notepad) can't move off their edge without a much
    /// bigger docking rework, so instead they lose height — top and/or bottom margin — wherever a
    /// dead zone overlaps their horizontal band, biased toward whichever half (upper/lower) the
    /// zone mostly sits in. Width is never touched (a narrower sidebar wraps its content badly).
    /// </summary>
    private void ApplySidebarDeadZoneAvoidance()
    {
        if (_viewModel is null) return;
        double rootW = Bounds.Width, rootH = Bounds.Height;
        if (rootW <= 0 || rootH <= 0) return;

        var zones = _viewModel.DeadZones;
        ApplySidebarInset(LyricsSidebarBorder, zones, rootW, rootH);
        ApplySidebarInset(QueueSidebarBorder, zones, rootW, rootH);
        ApplySidebarInset(SongWarsSidebarBorder, zones, rootW, rootH);
        ApplySidebarInset(NotepadSidebarBorder, zones, rootW, rootH);
    }

    private void ApplySidebarInset(Border sidebar, IReadOnlyList<DeadZone> zones, double rootW, double rootH)
    {
        if (!sidebar.IsVisible || sidebar.Bounds.Width <= 0 || sidebar.Bounds.Height <= 0 || zones.Count == 0)
        {
            sidebar.Margin = default;
            return;
        }

        var topLeft = sidebar.TranslatePoint(new Point(0, 0), this) ?? default;
        double x0 = Math.Clamp(topLeft.X / rootW, 0, 1);
        double x1 = Math.Clamp((topLeft.X + sidebar.Bounds.Width) / rootW, 0, 1);

        double marginTop = 0, marginBottom = 0;
        foreach (var dz in zones)
        {
            if (dz.X >= x1 || dz.X + dz.W <= x0) continue; // doesn't overlap this sidebar's column

            var zoneCenterY = dz.Y + dz.H / 2;
            if (zoneCenterY < 0.5)
            {
                var need = Math.Clamp(dz.Y + dz.H, 0, 1) * rootH - topLeft.Y;
                marginTop = Math.Max(marginTop, Math.Clamp(need, 0, sidebar.Bounds.Height));
            }
            else
            {
                var need = (topLeft.Y + sidebar.Bounds.Height) - Math.Clamp(dz.Y, 0, 1) * rootH;
                marginBottom = Math.Max(marginBottom, Math.Clamp(need, 0, sidebar.Bounds.Height));
            }
        }

        sidebar.Margin = new Thickness(0, marginTop, 0, marginBottom);
    }

    private void ApplyVisualizerLabelDeadZoneAvoidance()
    {
        if (_viewModel is null) return;
        double panelW = VisualizerSurfacePanel.Bounds.Width, panelH = VisualizerSurfacePanel.Bounds.Height;
        double labelW = VisualizerNameLabel.Bounds.Width, labelH = VisualizerNameLabel.Bounds.Height;
        const double baseLeft = 8, baseBottom = 6;

        var zones = _viewModel.DeadZones;
        if (panelW <= 0 || panelH <= 0 || labelW <= 0 || labelH <= 0 || zones.Count == 0)
        {
            VisualizerNameLabel.Margin = new Thickness(baseLeft, 0, 0, baseBottom);
            return;
        }

        double x0 = Math.Clamp(baseLeft / panelW, 0, 1);
        double y0 = Math.Clamp((panelH - baseBottom - labelH) / panelH, 0, 1);
        var (x, y) = DeadZoneHelper.Resolve(x0, y0, labelW / panelW, labelH / panelH, zones);

        double marginLeft = Math.Max(0, x * panelW);
        double marginBottom = Math.Max(0, panelH - (y * panelH + labelH));
        VisualizerNameLabel.Margin = new Thickness(marginLeft, 0, 0, marginBottom);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(NowPlayingViewModel.ActiveLyricIndex) &&
            _viewModel.ActiveLyricIndex >= 0 &&
            !_viewModel.IsTimedLyrics)
        {
            var container = LyricsList.ContainerFromIndex(_viewModel.ActiveLyricIndex);
            container?.BringIntoView();
        }

        if (e.PropertyName is nameof(NowPlayingViewModel.ShowYouTubeVideo) or
            nameof(NowPlayingViewModel.YouTubeVideoId) or
            nameof(NowPlayingViewModel.HasYouTubeVideo))
        {
            ApplyYouTubeVideoMode();
        }

        if (e.PropertyName is nameof(NowPlayingViewModel.ShowEmbeddedHtml) or
            nameof(NowPlayingViewModel.EmbeddedHtml) or
            nameof(NowPlayingViewModel.HasEmbeddedHtml))
        {
            ApplyEmbeddedHtmlMode();
        }

        if (e.PropertyName is nameof(NowPlayingViewModel.QueueUpcomingText) or
            nameof(NowPlayingViewModel.ShowQueue))
        {
            ScrollQueueToCurrent();
        }

        if (e.PropertyName is nameof(NowPlayingViewModel.ShowVisualizerSurface) or
            nameof(NowPlayingViewModel.SelectedVisualizer))
        {
            ApplyVisualizerPanelDeadZoneAvoidance();
            ApplyVisualizerLabelDeadZoneAvoidance();
        }

        if (e.PropertyName is nameof(NowPlayingViewModel.ShowLyrics) or
            nameof(NowPlayingViewModel.ShowQueue) or
            nameof(NowPlayingViewModel.ShowSongWarsPanel) or
            nameof(NowPlayingViewModel.ShowNotepadPanel))
        {
            ApplySidebarDeadZoneAvoidance();
        }
    }

    private void ScrollQueueToCurrent()
    {
        if (_viewModel is not { ShowQueue: true })
        {
            return;
        }

        var current = _viewModel.QueueItems.FirstOrDefault(item => item.IsCurrent);
        if (current is not null)
        {
            QueueList.ScrollIntoView(current);
        }
    }

    private void OnSongWarsPopOut(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NowPlayingViewModel vm)
            vm.SongWarsPopOutRequested?.Invoke();
    }

    // ── Notepads ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Nil state (nothing playing) has no docked notepad panel to open — the docked panel only
    /// exists inside the "playing" layout — so this creates a notepad and pops it straight into
    /// its own window instead.
    /// </summary>
    private void OnNilStateOpenNotepad(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var notepad = _viewModel.Notepads.NewNotepad();
        _viewModel.Notepads.RequestPopOut(notepad);
    }

    private void OnNotepadTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: NotepadViewModel notepad } && _viewModel is not null)
            _viewModel.Notepads.SelectedNotepad = notepad;
    }

    private void OnNotepadPopOut(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.Notepads.SelectedNotepad is { } notepad)
            _viewModel.Notepads.RequestPopOut(notepad);
    }

    private void OnNotepadClose(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.Notepads.SelectedNotepad is { } notepad)
            _viewModel.Notepads.CloseNotepad(notepad);
    }

    private void OnNotepadSaveToTrack(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not { Notepads.SelectedNotepad: { } notepad }) return;

        var trackPath = _viewModel.CurrentTrackPath;
        if (trackPath is null)
        {
            _viewModel.Notepads.StatusMessage = "No local track playing — nothing to embed into.";
            return;
        }

        try
        {
            _viewModel.Notepads.SaveToTrack(notepad, trackPath);
            _viewModel.Notepads.StatusMessage = $"Saved to {Path.GetFileName(trackPath)}.";
        }
        catch (Exception ex)
        {
            _viewModel.Notepads.StatusMessage = $"Couldn't save: {ex.Message}";
        }
    }

    private void OnNotepadPopOutRequested(NotepadViewModel notepad)
    {
        var window = new NotepadWindow { DataContext = notepad };
        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
    }

    private void OnInspectLyrics(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NowPlayingViewModel vm) return;
        if (vm.CurrentLyrics is null) return;
        if (_lyricsInspectorWindow is { IsVisible: true }) { _lyricsInspectorWindow.Activate(); return; }

        _lyricsInspectorWindow = new LyricsInspectorWindow(
            vm.Title,
            vm.Artist,
            vm.Album,
            vm.CurrentLyrics,
            vm.PositionSeconds,
            vm.CurrentTrackPath);
        _lyricsInspectorWindow.Closed += (_, _) => _lyricsInspectorWindow = null;
        _lyricsInspectorWindow.Show(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
    }

    private void OnToggleTimeDisplay(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        (DataContext as NowPlayingViewModel)?.ToggleTimeDisplay();
    }

    private QueueItemViewModel? SelectedQueueItem => QueueList.SelectedItem as QueueItemViewModel;

    private async void OnQueueItemDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is NowPlayingViewModel vm && SelectedQueueItem is { } item)
        {
            await vm.PlayQueueItemAsync(item);
        }
    }

    private void OnQueueKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Delete &&
            DataContext is NowPlayingViewModel vm &&
            SelectedQueueItem is { } item)
        {
            e.Handled = true;
            vm.RemoveQueueItem(item);
        }
    }

    private async void OnQueuePlay(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NowPlayingViewModel vm && SelectedQueueItem is { } item)
        {
            await vm.PlayQueueItemAsync(item);
        }
    }

    private void OnQueuePlayNext(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NowPlayingViewModel vm && SelectedQueueItem is { } item)
        {
            vm.PlayQueueItemNext(item);
        }
    }

    private void OnQueueMoveUp(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NowPlayingViewModel vm && SelectedQueueItem is { } item)
        {
            vm.MoveQueueItemUp(item);
        }
    }

    private void OnQueueMoveDown(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NowPlayingViewModel vm && SelectedQueueItem is { } item)
        {
            vm.MoveQueueItemDown(item);
        }
    }

    private void OnQueueRemove(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NowPlayingViewModel vm && SelectedQueueItem is { } item)
        {
            vm.RemoveQueueItem(item);
        }
    }

    private void OnQueueClear(object? sender, RoutedEventArgs e)
    {
        (DataContext as NowPlayingViewModel)?.ClearQueue();
    }

    private async void OnQueueEditTags(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            SelectedQueueItem is not { } item ||
            !File.Exists(item.Path))
        {
            return;
        }

        var saved = await TagEditorWindow.EditAsync(owner, item.Path);
        if (saved &&
            owner.DataContext is MainWindowViewModel shell)
        {
            await shell.Library.ScanPathsAsync([item.Path]);
            await shell.NowPlaying.RefreshCurrentTrackMetadataAsync(item.Path);
        }
    }

    private async void OnQueueContentWarnings(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner ||
            SelectedQueueItem is not { } item ||
            !File.Exists(item.Path))
        {
            return;
        }

        await ContentWarningEditorWindow.ShowAsync(owner, item.Path);
    }

    private async void OnQueueSaveAsPlaylist(object? sender, RoutedEventArgs e)
    {
        // Playlist creation lives on the shell view model; reach it through the window.
        if (TopLevel.GetTopLevel(this) is not Window { DataContext: MainWindowViewModel shell } owner ||
            shell.NowPlaying.Queue.IsEmpty)
        {
            return;
        }

        var name = await NameInputWindow.PromptAsync(owner, "Save Queue as Playlist", "Playlist name:", "Queue");
        if (!string.IsNullOrWhiteSpace(name))
        {
            shell.Playlists.CreatePlaylist(name, shell.NowPlaying.Queue.Items);
        }
    }

    private async void OnQueueAddFiles(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NowPlayingViewModel vm || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add files to queue",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Audio files")
                {
                    Patterns = SupportedAudioFormats.Extensions.Select(ext => "*" + ext).ToArray(),
                },
                FilePickerFileTypes.All,
            ],
        });

        var paths = files
            .Select(file => file.TryGetLocalPath())
            .Where(path => path is not null && File.Exists(path) && SupportedAudioFormats.IsSupportedExtension(path))
            .Select(path => path!)
            .ToList();

        if (paths.Count > 0)
        {
            await vm.QueueFilesAsync(paths, playIfQueueWasEmpty: true);
        }
    }

    private void OnCycleRepeat(object? sender, RoutedEventArgs e)
    {
        (DataContext as NowPlayingViewModel)?.CycleRepeat();
    }

    private void OnToggleMute(object? sender, RoutedEventArgs e)
    {
        (DataContext as NowPlayingViewModel)?.ToggleMute();
    }

    private void OnCycleSurfaceMode(object? sender, RoutedEventArgs e)
    {
        (DataContext as NowPlayingViewModel)?.CycleSurfaceMode();
    }

    private void OnExitSurfaceMode(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NowPlayingViewModel vm)
        {
            vm.UseArtworkSurface();
        }
    }

    private void OnOpenMiniPlayer(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window mainWindow || DataContext is not NowPlayingViewModel vm)
        {
            return;
        }

        var mini = new MiniPlayerWindow { DataContext = vm };
        mini.ExpandRequested += (_, _) =>
        {
            // Returning to the full player preserves playback state and position:
            // the engine never stops, only the chrome changes.
            mainWindow.Show();
            mini.Close();
        };
        mini.Closed += (_, _) =>
        {
            if (!mainWindow.IsVisible)
            {
                mainWindow.Show();
            }
        };

        mini.Show();
        mainWindow.Hide();
    }

    private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NowPlayingViewModel vm || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open audio file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Audio files")
                {
                    Patterns = SupportedAudioFormats.Extensions.Select(ext => "*" + ext).ToArray(),
                },
                FilePickerFileTypes.All,
            ],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
        {
            await vm.LoadTrackAsync(path);
        }
    }

    private async void OnOpenUrlClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NowPlayingViewModel vm || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var url = await OpenUrlWindow.PromptAsync(topLevel);
        if (!string.IsNullOrWhiteSpace(url))
        {
            await vm.LoadUrlAsync(url);
        }
    }

    private void ApplyYouTubeVideoMode()
    {
        if (_viewModel is not { ShowYouTubeVideo: true, HasYouTubeVideo: true })
        {
            StopYouTubeVideoMode();
            return;
        }

        if (_youTubeWebView is not null &&
            string.Equals(_loadedYouTubeVideoId, _viewModel.YouTubeVideoId, StringComparison.Ordinal))
        {
            return;
        }

        StopYouTubeVideoMode();

        _loadedYouTubeVideoId = _viewModel.YouTubeVideoId;
        _youTubeWebView = new WebViewControl.WebView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        YouTubeVideoHost.Content = _youTubeWebView;

        try
        {
            _youTubeVideoServer = YouTubeVideoPageServer.Start(
                _viewModel.YouTubeVideoId,
                _viewModel.PositionSeconds,
                _viewModel.IsPlaying);
            _youTubeWebView.LoadUrl(_youTubeVideoServer.Url);

            _youTubeSyncTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500),
            };
            _youTubeSyncTimer.Tick += OnYouTubeSyncTimerTick;
            _youTubeSyncTimer.Start();
        }
        catch
        {
            StopYouTubeVideoMode();
            _viewModel.ShowYouTubeVideo = false;
        }
    }

    private void OnYouTubeSyncTimerTick(object? sender, EventArgs e) => SyncYouTubeVideoFrame();

    private void SyncYouTubeVideoFrame()
    {
        if (_viewModel is not { ShowYouTubeVideo: true } || _youTubeWebView is null)
        {
            return;
        }

        var seconds = YouTubeVideoPageServer.FormatSeconds(_viewModel.PositionSeconds);
        var shouldPlay = _viewModel.IsPlaying ? "true" : "false";
        try
        {
            _youTubeWebView.ExecuteScript($"window.ytpSync && window.ytpSync({seconds}, {shouldPlay})");
        }
        catch
        {
        }
    }

    private void StopYouTubeVideoMode()
    {
        _youTubeSyncTimer?.Stop();
        if (_youTubeSyncTimer is not null)
        {
            _youTubeSyncTimer.Tick -= OnYouTubeSyncTimerTick;
        }

        _youTubeSyncTimer = null;

        try
        {
            _youTubeWebView?.ExecuteScript("window.ytpPause && window.ytpPause()");
        }
        catch
        {
        }

        YouTubeVideoHost.Content = null;
        _youTubeWebView?.Dispose();
        _youTubeWebView = null;
        _youTubeVideoServer?.Dispose();
        _youTubeVideoServer = null;
        _loadedYouTubeVideoId = string.Empty;
    }

    private void ApplyEmbeddedHtmlMode()
    {
        if (_viewModel is not { ShowEmbeddedHtml: true, EmbeddedHtml: { } context })
        {
            StopEmbeddedHtmlMode();
            return;
        }

        if (_embeddedControl is not null &&
            string.Equals(_loadedEmbeddedHtmlId, context.Id, StringComparison.Ordinal))
        {
            return;
        }

        StopEmbeddedHtmlMode();

        _loadedEmbeddedHtmlId = context.Id;

#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            // GPU-accelerated path: WebView2 renders via DirectComposition — no OSR bitmap roundtrip.
            // The host is created once and reused across capsule track changes so the HWND
            // stays alive and WebView2 never has to re-initialize (~400ms overhead).
            _persistentWv2 ??= new WebView2Host
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                UserDataFolder = Path.Combine(Path.GetTempPath(), "spectralis-embedded-webview2"),
            };
            _embeddedControl = _persistentWv2;
            _embeddedHost = _persistentWv2;
        }
        else
#endif
        {
            var cefWebView = new WebViewControl.WebView
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            var cefHost = new CefGlueWebViewHost(cefWebView);
            // Pull model: bridge reads the volatile cache populated by the timer.
            // No cross-process ExecuteScript calls are made for CefGlue — the JS
            // rAF loop pulls via spectralisBridge.getFrameJson() on its own cadence.
            cefHost.FrameJsonProvider = () => _latestFrameJson;
            _embeddedControl = cefWebView;
            _embeddedHost = cefHost;
        }

        _embeddedHost.NavigationCompleted += OnEmbeddedNavigationCompleted;
        _embeddedHost.NavigationFailed += OnEmbeddedNavigationFailed;
        var isAlbumWorld = _viewModel?.IsAlbumWorldShowingWorld ?? false;
        _embeddedService = new WebViewHostService(_embeddedHost, storeKey: isAlbumWorld ? null : context.Id, isAlbumWorld: isAlbumWorld);
        _embeddedService.PlayTrackRequested += OnEmbeddedPlayTrackRequested;
        _embeddedService.PauseRequested += OnEmbeddedPauseRequested;
        _embeddedService.ResumeRequested += OnEmbeddedResumeRequested;
        _embeddedService.SeekRequested += OnEmbeddedSeekRequested;
        _embeddedService.ExitWorldRequested += OnEmbeddedExitRequested;
        _embeddedService.SaveBookmarkRequested += OnEmbeddedSaveBookmark;
        EmbeddedHtmlHost.Content = _embeddedControl;

        try
        {
            var document = BuildEmbeddedHtmlDocument(context, _viewModel);
            AppLogPaths.AppendTimestamped(_webviewPerfLog,
                $"[EMBEDDED] navigate id={context.Id} chars={document.Length:n0} " +
                $"utf8={Encoding.UTF8.GetByteCount(document):n0} hash={HashShort(document)}");
            NavigateEmbeddedHtmlDocument(context, document, isAlbumWorld);
        }
        catch (Exception ex)
        {
            AppLogPaths.AppendTimestamped(_webviewPerfLog,
                $"[EMBEDDED] navigate failed id={context.Id}: {ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}");
            StopEmbeddedHtmlMode();
            if (_viewModel is not null)
                _viewModel.ShowEmbeddedHtml = false;
        }
    }

    private void NavigateEmbeddedHtmlDocument(EmbeddedHtmlContext context, string document, bool isAlbumWorld)
    {
        if (_embeddedHost is null)
        {
            return;
        }

        if (!isAlbumWorld ||
            string.IsNullOrWhiteSpace(context.SourceDirectory) ||
            !Directory.Exists(context.SourceDirectory))
        {
            _embeddedHost.NavigateToString(document);
            return;
        }

        var hostName = BuildAlbumWorldHostName(context.SourceDirectory);
        var fileName = "__spectralis_album_world.html";
        var hostedPath = Path.Combine(context.SourceDirectory, fileName);
        File.WriteAllText(hostedPath, document, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        _embeddedHost.MapVirtualHost(hostName, context.SourceDirectory);
        _embeddedHost.Navigate(new Uri($"https://{hostName}/{fileName}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"));
    }

    private static string BuildAlbumWorldHostName(string sourceDirectory)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(sourceDirectory)));
        return $"album-{Convert.ToHexString(hash, 0, 8).ToLowerInvariant()}.spectralis.local";
    }

    private void OnEmbeddedPauseRequested(object? sender, EventArgs e)
    {
        if (_viewModel is { IsPlaying: true })
        {
            _viewModel.TogglePlayback();
        }
    }

    private void OnEmbeddedResumeRequested(object? sender, EventArgs e)
    {
        // If this resume came from a story explainer with a visualizer queued behind it,
        // swap surfaces first — ApplyEmbeddedHtmlMode picks up the change and re-navigates.
        _viewModel?.TryAdvancePastStory();

        if (_viewModel is { IsPlaying: false, HasTrack: true })
        {
            _viewModel.TogglePlayback();
        }
    }

    private void OnEmbeddedSeekRequested(object? sender, double seconds)
    {
        if (_viewModel is not null)
        {
            _viewModel.PositionSeconds = Math.Clamp(seconds, 0, _viewModel.LengthSeconds);
        }
    }

    private void OnEmbeddedPlayTrackRequested(object? sender, Spectralis.Core.Integrations.Web.AlbumTrackPlayRequest req)
    {
        _viewModel?.AlbumPlayTrackDelegate?.Invoke(req.TrackId, req.PositionSeconds);
    }

    private void OnAlbumWorldTrackChanged(AlbumWorldTrackBridgeState state)
    {
        if (_viewModel?.IsAlbumWorldShowingWorld != true || _embeddedService is null)
            return;

        _ = _embeddedService.SendTrackChangedAsync(
            state.TrackId,
            state.Title,
            state.Artist,
            state.DurationSeconds);
    }

    private void OnAlbumWorldTrackCompleted(string trackId, double playedSeconds)
    {
        if (_viewModel?.IsAlbumWorldShowingWorld != true || _embeddedService is null)
            return;

        _ = _embeddedService.SendTrackCompletedAsync(trackId, playedSeconds);
    }

    private void OnEmbeddedExitRequested(object? sender, EventArgs e)
    {
        if (_viewModel?.IsAlbumWorldShowingWorld == true)
            _viewModel.AlbumWorldExitDelegate?.Invoke();
        else
            _viewModel?.UseArtworkSurface();
    }

    private void OnEmbeddedSaveBookmark(object? sender, Spectralis.Core.Integrations.Web.AlbumBookmarkRequest req)
    {
        var worldDir = _viewModel?.AlbumWorldDir;
        if (worldDir is null) return;
        Spectralis.Core.Capsule.AlbumWorldSessionStore.SaveBookmark(worldDir, req.TrackId, req.Label);
    }

    private void OnEmbeddedNavigationFailed(object? sender, EventArgs e)
    {
        SpectralisLog.Warn("Embedded HTML navigation failed; falling back to visualizer.");
        StopEmbeddedHtmlMode();
        if (_viewModel is not null)
        {
            _viewModel.ShowEmbeddedHtml = false;
            _viewModel.UseArtworkSurface();
        }
    }

    private void OnEmbeddedNavigationCompleted(object? sender, EventArgs e)
    {
        _embeddedFramePushTimer?.Stop();
        _lastPushedTime = double.MinValue;
        _lastPushedActive = false;
        _pushCount = 0;
        _skipCount = 0;
        _lateCount = 0;
        _maxIntervalMs = 0;
        _sumIntervalMs = 0;
        _intervalSamples = 0;
        _lastStatsTick = 0;
        _pushClock.Restart();
        var mode = _embeddedHost is CefGlueWebViewHost ? "pull/CefGlue" : "push/WebView2";
        AppLogPaths.AppendTimestamped(_webviewPerfLog, $"[EMBEDDED] frame pump start — mode={mode} target=16ms");
        _embeddedFramePushTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Normal,
            OnEmbeddedFramePushTick);
        _embeddedFramePushTimer.Start();

        // If this is an album world HTML, send the initial state so the map can populate.
        if (_viewModel is { IsAlbumWorldShowingWorld: true, AlbumWorldReadyJson: { } readyJson } && _embeddedService is not null)
            _ = _embeddedService.SendReadyAsync(readyJson);
    }

    private void OnEmbeddedFramePushTick(object? sender, EventArgs e)
    {
        if (_embeddedHost is null || _viewModel is not { ShowEmbeddedHtml: true })
            return;

        var elapsedMs = _pushClock.Elapsed.TotalMilliseconds;
        _pushClock.Restart();

        if (_intervalSamples > 0)
        {
            _sumIntervalMs += elapsedMs;
            if (elapsedMs > _maxIntervalMs) _maxIntervalMs = elapsedMs;
            if (elapsedMs > 50) _lateCount++;
        }
        _intervalSamples++;

        // Always refresh the volatile frame cache. CefGlue reads this synchronously
        // from the renderer process when the JS rAF loop calls getFrameJson(); no IPC
        // push is needed on that path. The timer only exists here to keep the cache fresh.
        var json = BuildEmbeddedFrameJson();
        _latestFrameJson = json;
        var active = _viewModel.IsPlaying;
        var time = _viewModel.PositionSeconds;

        // WebView2 uses a push model: ExecuteScript queues the frame in JS so the rAF
        // pump can pick it up without a C#→renderer IPC pull. _embeddedExecPending
        // limits the queue depth to 1 (WebView2's ExecuteScriptAsync is truly async).
        if (_embeddedHost is CefGlueWebViewHost || string.IsNullOrEmpty(json))
        {
            // CefGlue: pull-only, no IPC push. Count as "skipped" only for comparability
            // with the old stats; real throughput is now driven by the renderer's rAF.
            _skipCount++;
            FlushStatsIfDue();
            return;
        }

        if (!active && !_lastPushedActive && Math.Abs(time - _lastPushedTime) < 0.05)
        {
            _skipCount++;
        }
        else if (_embeddedExecPending)
        {
            _skipCount++;
        }
        else
        {
            var execSw = Stopwatch.StartNew();
            _embeddedExecPending = true;
            _ = _embeddedHost.ExecuteScriptAsync(
                $"window.__spectralisReceiveFrame && window.__spectralisReceiveFrame({json})")
                .ContinueWith(_ => _embeddedExecPending = false);
            execSw.Stop();

            _lastPushedTime = time;
            _lastPushedActive = active;
            _pushCount++;

            if (execSw.ElapsedMilliseconds > 10)
            {
                AppLogPaths.AppendTimestamped(_webviewPerfLog,
                    $"[SLOW-PUSH] interval={elapsedMs:F1}ms exec={execSw.ElapsedMilliseconds}ms active={active} t={time:F2}s");
            }
        }

        FlushStatsIfDue();
    }

    private void FlushStatsIfDue()
    {
        var wallSec = (long)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 5000);
        if (wallSec == _lastStatsTick) return;

        _lastStatsTick = wallSec;
        var avgMs = _intervalSamples > 1 ? _sumIntervalMs / (_intervalSamples - 1) : 0;
        var mode = _embeddedHost is CefGlueWebViewHost ? "pull" : "push";
        AppLogPaths.AppendTimestamped(_webviewPerfLog,
            $"[STATS:{mode}] pushed={_pushCount} skipped={_skipCount} late={_lateCount} " +
            $"avg={avgMs:F1}ms max={_maxIntervalMs:F1}ms samples={_intervalSamples}");
        _pushCount = 0;
        _skipCount = 0;
        _lateCount = 0;
        _maxIntervalMs = 0;
        _sumIntervalMs = 0;
        _intervalSamples = 0;
    }

    private string BuildEmbeddedFrameJson()
    {
        if (_viewModel is not { ShowEmbeddedHtml: true })
        {
            return string.Empty;
        }

        var frame = _viewModel.Engine.GetVisualizerFrame();
        return JsonSerializer.Serialize(new
        {
            levels = SampleSpectrum(frame.Spectrum, 32),
            peak = Math.Clamp(frame.PeakLevel, 0f, 1.25f),
            rms = Math.Clamp(frame.RmsLevel, 0f, 1.25f),
            active = _viewModel.IsPlaying,
            time = _viewModel.PositionSeconds,
            trackId = _viewModel.IsAlbumWorldActive ? _viewModel.AlbumWorldCurrentTrackId : string.Empty,
        });
    }

    private void StopEmbeddedHtmlMode()
    {
        if (_embeddedFramePushTimer is not null)
        {
            _embeddedFramePushTimer.Stop();
            AppLogPaths.AppendTimestamped(_webviewPerfLog,
                $"[EMBEDDED] frame pump stop — total pushed={_pushCount} skipped={_skipCount} late={_lateCount}");
        }
        _embeddedFramePushTimer = null;
        _lastPushedTime = double.MinValue;
        _lastPushedActive = false;
        _embeddedExecPending = false;

        if (_embeddedService is not null)
        {
            _embeddedService.PlayTrackRequested -= OnEmbeddedPlayTrackRequested;
            _embeddedService.PauseRequested -= OnEmbeddedPauseRequested;
            _embeddedService.ResumeRequested -= OnEmbeddedResumeRequested;
            _embeddedService.SeekRequested -= OnEmbeddedSeekRequested;
            _embeddedService.ExitWorldRequested -= OnEmbeddedExitRequested;
            _embeddedService.SaveBookmarkRequested -= OnEmbeddedSaveBookmark;
            _embeddedService.Dispose();
            _embeddedService = null;
        }

        if (_embeddedHost is not null)
        {
            _embeddedHost.NavigationCompleted -= OnEmbeddedNavigationCompleted;
            _embeddedHost.NavigationFailed -= OnEmbeddedNavigationFailed;
            if (_embeddedHost is CefGlueWebViewHost cefHost)
                cefHost.FrameJsonProvider = null;
        }

        // Keep the persistent WV2 host in the visual tree across track changes and
        // NavigationFailed fallback calls. Check EmbeddedHtmlHost.Content rather than
        // _embeddedHost because _embeddedHost is already null on re-entrant calls
        // (e.g. ShowEmbeddedHtml=false fires ApplyEmbeddedHtmlMode which calls this
        // again after OnEmbeddedNavigationFailed already cleared _embeddedHost).
        // Removing _persistentWv2 from the visual tree would destroy its HWND — if the
        // controller was live, that closes it and the browser process exits, leaving the
        // cached CoreWebView2Environment stale so the next capsule's CreateController fails.
#if WINDOWS
        var keepPersistentWv2 = _persistentWv2 is not null &&
                                 ReferenceEquals(EmbeddedHtmlHost.Content, _persistentWv2);
#else
        const bool keepPersistentWv2 = false;
#endif
        if (!keepPersistentWv2)
        {
            EmbeddedHtmlHost.Content = null;
            _embeddedHost?.Dispose();
        }

        _embeddedHost = null;
        _embeddedControl = null;
        _loadedEmbeddedHtmlId = string.Empty;
    }

    private string BuildEmbeddedHtmlDocument(EmbeddedHtmlContext context, NowPlayingViewModel? vm)
    {
        AppLogPaths.AppendTimestamped(_webviewPerfLog,
            $"[EMBEDDED] build start id={context.Id} version={context.Version ?? "<none>"} " +
            $"htmlBytes={context.HtmlBytes.Length:n0} binaryAssets={context.BinaryAssets.Count} " +
            $"binaryBytes={context.BinaryAssets.Values.Sum(static bytes => bytes.Length):n0} " +
            $"textAssets={context.TextAssets.Count} textChars={context.TextAssets.Values.Sum(static text => text.Length):n0}");

        var isAlbumWorld = vm?.IsAlbumWorldShowingWorld ?? false;

        // Album worlds have no current track at document-build time; skip spectral.meta.
        var meta = isAlbumWorld ? EmbeddedTrackMeta.Empty : BuildEmbeddedTrackMeta(vm);

        return EmbeddedHtmlDocument.Build(
            context,
            meta,
            isAlbumWorld,
            onStage: (stage, html) => LogDocumentStage(context.Id, stage, html),
            log: message => AppLogPaths.AppendTimestamped(_webviewPerfLog, message));
    }

    private static void LogDocumentStage(string contextId, string stage, string html) =>
        AppLogPaths.AppendTimestamped(_webviewPerfLog,
            $"[EMBEDDED] build id={contextId} stage={stage} chars={html.Length:n0} " +
            $"utf8={Encoding.UTF8.GetByteCount(html):n0} hash={HashShort(html)}");

    private static string HashShort(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>Snapshots the current track's metadata for <c>window.spectral.meta</c> injection.</summary>
    private static EmbeddedTrackMeta BuildEmbeddedTrackMeta(NowPlayingViewModel? vm)
    {
        var track = vm?.Engine?.CurrentTrack;

        byte[]? artworkBytes = null;
        string? artworkMime = null;
        if (track?.CoverArt is { Length: > 0 } art)
        {
            artworkBytes = art;
            artworkMime = string.IsNullOrWhiteSpace(track.CoverArtMimeType) ? "image/jpeg" : track.CoverArtMimeType;
        }
        else if (vm?.CoverArtBytes is { Length: > 0 } vmArt)
        {
            artworkBytes = vmArt;
            artworkMime = "image/jpeg";
        }

        AppLogPaths.AppendTimestamped(_webviewPerfLog,
            $"[EMBEDDED] meta artwork bytes={(artworkBytes?.Length ?? 0):n0}");

        return new EmbeddedTrackMeta(
            Title: vm?.Title ?? track?.DisplayTitle ?? string.Empty,
            Artist: vm?.Artist ?? track?.Artist ?? string.Empty,
            Album: vm?.Album ?? track?.Album ?? string.Empty,
            AlbumArtist: track?.AlbumArtist ?? string.Empty,
            Genre: track?.Genre ?? string.Empty,
            Year: (int)(track?.Year ?? 0),
            TrackNumber: (int)(track?.TrackNumber ?? 0),
            DurationSeconds: vm?.LengthSeconds ?? track?.Duration.TotalSeconds ?? 0.0,
            Bpm: track?.Bpm,
            MusicalKey: track?.MusicalKey,
            SampleRate: track?.SampleRateHz ?? 0,
            Channels: track?.Channels ?? 0,
            ArtworkBytes: artworkBytes,
            ArtworkMimeType: artworkMime);
    }

    private static float[] SampleSpectrum(float[] spectrum, int count)
    {
        if (spectrum.Length == 0)
        {
            return new float[count];
        }

        var result = new float[count];
        var ratio = (double)spectrum.Length / count;
        for (var i = 0; i < count; i++)
        {
            var src = (int)(i * ratio);
            result[i] = Math.Clamp(spectrum[Math.Min(src, spectrum.Length - 1)], 0, 1.25f);
        }

        return result;
    }
}
