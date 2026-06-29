using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Spectralis.App.Controls;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.Core.Capsule;
using Spectralis.Core.Common;
using Spectralis.Core.Integrations.Spotify;
using Spectralis.Core.Platform;

namespace Spectralis.App.Views;

public partial class MainWindow : Window
{
    private const int ClipboardUrlHistoryLimit = 96;

    private static readonly Regex ClipboardUrlRegex = new(
        @"https?://[^\s<>()""']+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private bool _placementApplied;
    private readonly DispatcherTimer _clipboardMonitorTimer;
    private readonly HashSet<string> _clipboardUrlHistory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _clipboardUrlHistoryOrder = new();
    private readonly HashSet<string> _toastedTrackHistory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _toastedTrackHistoryOrder = new();
    private readonly OpenUrlService _clipboardUrlResolver = new();
    private string _lastClipboardText = string.Empty;
    private bool _checkingClipboard;
    private ITrayService? _trayService;
    private bool _hiddenToTray;
    private bool _forceClose;
    private bool _closePromptOpen;
    private bool _trayEventsAttached;
    private bool _trayVmSubscribed;
    private bool _trayNowPlayingSubscribed;

    public MainWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif

        _clipboardMonitorTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(1200),
            DispatcherPriority.Background,
            OnClipboardMonitorTick);

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnWindowKeyDown, handledEventsToo: true);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.Capsules.TrustCreatorPrompt = PromptTrustCreatorAsync;
                vm.NowPlaying.ContentWarningPrompt = PromptContentWarningAsync;
                InitializeSpotifyPlaybackHost(vm);
                InitializeTraySupport(vm);
            }

            if (IsVisible)
            {
                ApplySavedWindowPlacement();
            }
        };
        Opened += async (_, _) =>
        {
            ApplySavedWindowPlacement();
            _clipboardMonitorTimer.Start();
            InitializeMediaSession();
            UpdateTrayState();
            await ShowConsentDialogIfNeededAsync();
        };
        Closed += (_, _) =>
        {
            _clipboardMonitorTimer.Stop();
            _trayService?.Dispose();
            _trayService = null;
        };
        Closing += (_, e) =>
        {
            if (!_forceClose &&
                !_hiddenToTray &&
                DataContext is MainWindowViewModel vm &&
                vm.AppSettings.CloseToTray)
            {
                e.Cancel = true;
                _ = HandleCloseButtonAsync();
                return;
            }

            SaveWindowPlacement();
            _mediaSession?.Dispose();
            _mediaSession = null;
        };
    }

    // ── OS media session (SMTC on Windows) ──────────────────────────────────

    private void InitializeTraySupport(MainWindowViewModel vm)
    {
        _trayService ??= new AvaloniaTrayService();
        if (!_trayEventsAttached)
        {
            _trayService.OpenRequested += (_, _) => Dispatcher.UIThread.Post(RestoreFromTray);
            _trayService.PlayMostRecentRequested += (_, _) => Dispatcher.UIThread.Post(() => _ = PlayMostRecentFromTrayAsync());
            _trayService.ExitRequested += (_, _) => Dispatcher.UIThread.Post(RequestAppExit);
            _trayEventsAttached = true;
        }

        if (!_trayVmSubscribed)
        {
            vm.PropertyChanged += OnTrayViewModelChanged;
            _trayVmSubscribed = true;
        }

        if (!_trayNowPlayingSubscribed)
        {
            vm.NowPlaying.PropertyChanged += OnTrayNowPlayingChanged;
            _trayNowPlayingSubscribed = true;
        }
    }

    private void HideToTray()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        InitializeTraySupport(vm);
        SaveWindowPlacement();
        _hiddenToTray = true;
        ShowInTaskbar = false;
        UpdateTrayState();
        _trayService?.Show(BuildTrayTooltip(vm));
        Hide();
    }

    private void RestoreFromTray()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        _hiddenToTray = false;
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
        UpdateTrayState();
        _trayService?.Hide();
    }

    private async Task HandleCloseButtonAsync()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            RequestAppExit();
            return;
        }

        if (!vm.AppSettings.CloseToTray)
        {
            RequestAppExit();
            return;
        }

        if (!vm.AppSettings.CloseToTrayPromptDismissed)
        {
            if (_closePromptOpen)
            {
                return;
            }

            _closePromptOpen = true;
            var result = await CloseToTrayPromptWindow.ShowAsync(this, vm.AppSettings.CloseToTray);
            _closePromptOpen = false;
            if (!result.Accepted)
            {
                return;
            }

            vm.Settings.CloseToTray = result.CloseToTray;
            vm.AppSettings.CloseToTrayPromptDismissed = true;
            AppSettingsStore.Save(vm.AppSettings);

            if (!result.CloseToTray)
            {
                RequestAppExit();
                return;
            }
        }

        HideToTray();
    }

    private async Task PlayMostRecentFromTrayAsync()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        RestoreFromTray();
        if (vm.NowPlaying.HasTrack)
        {
            if (!vm.NowPlaying.IsPlaying)
            {
                vm.NowPlaying.TogglePlayback();
            }

            return;
        }

        await vm.PlayMostRecentSongAsync();
    }

    private void RequestAppExit()
    {
        _forceClose = true;
        _trayService?.Hide();
        Close();
    }

    private void UpdateTrayState()
    {
        if (_trayService is null || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (vm.NowPlaying.HasTrack)
        {
            _trayService.UpdateNowPlaying(vm.NowPlaying.Title, vm.NowPlaying.Artist);
        }
        else
        {
            _trayService.UpdateNowPlaying("Spectralis", vm.IdleActivityText);
        }

        if (_hiddenToTray)
        {
            _trayService.Show(BuildTrayTooltip(vm));
        }
    }

    private void OnTrayViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IdleActivityText) or nameof(MainWindowViewModel.StatusText))
        {
            UpdateTrayState();
        }
    }

    private void OnTrayNowPlayingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NowPlayingViewModel.PositionSeconds)
            or nameof(NowPlayingViewModel.LengthSeconds))
        {
            if (_hiddenToTray)
            {
                UpdateTrayState();
            }

            return;
        }

        if (e.PropertyName is nameof(NowPlayingViewModel.HasTrack)
            or nameof(NowPlayingViewModel.Title)
            or nameof(NowPlayingViewModel.Artist)
            or nameof(NowPlayingViewModel.IsPlaying))
        {
            UpdateTrayState();
        }
    }

    private static string BuildTrayTooltip(MainWindowViewModel vm)
    {
        if (!vm.NowPlaying.HasTrack)
        {
            return $"Spectralis\n{vm.IdleActivityText}";
        }

        var state = vm.NowPlaying.IsPlaying ? "Playing" : "Paused";
        var title = string.IsNullOrWhiteSpace(vm.NowPlaying.Artist)
            ? vm.NowPlaying.Title
            : $"{vm.NowPlaying.Artist} - {vm.NowPlaying.Title}";
        return $"{state}: {title}\n{vm.NowPlaying.PositionText} / {vm.NowPlaying.LengthText}";
    }

    private Spectralis.Core.Platform.IMediaSessionService? _mediaSession;

    private async Task ShowConsentDialogIfNeededAsync()
    {
        if (DataContext is not MainWindowViewModel vm || vm.AppSettings.ExternalApiConsentAccepted)
        {
            return;
        }

        var proceeded = await ConfirmWindow.ShowAsync(
            this,
            "External Services",
            "Spectralis uses external APIs and services such as YouTube, SoundCloud, Suno, Spotify, and others." +
            " These integrations are not always stable and may break in later releases.\n\n" +
            "Would you like to proceed?",
            "Proceed",
            "Exit");

        if (proceeded)
        {
            vm.AppSettings.ExternalApiConsentAccepted = true;
            AppSettingsStore.Save(vm.AppSettings);
        }
        else
        {
            Close();
        }
    }

    private bool _spotifyHostInitialized;

    /// <summary>
    /// Creates the hidden Spotify Web Playback SDK host once per window. Must live in the
    /// visual tree (SpotifyHostSlot) for WebView2Host's native control to initialize, but
    /// nothing actually connects to Spotify until the user clicks "Play Spotify".
    /// </summary>
    private void InitializeSpotifyPlaybackHost(MainWindowViewModel vm)
    {
        if (_spotifyHostInitialized || !OperatingSystem.IsWindows())
        {
            return;
        }

        _spotifyHostInitialized = true;
#if WINDOWS
        var webView = new WebView2Host
        {
            UserDataFolder = Path.Combine(Path.GetTempPath(), "spectralis-spotify-webview2"),
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required --disable-features=AudioServiceOutOfProcess"
        };
        SpotifyHostSlot.Content = webView;

        var spotify = new SpotifyService();
        vm.NowPlaying.SpotifyHost = new SpotifyPlaybackHostService(
            webView,
            spotify,
            () => SpotifyClientIdProvider.ResolveClientId(vm.AppSettings.SpotifyCustomClientId));
#endif
    }

    private void InitializeMediaSession()
    {
        if (_mediaSession is not null || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        vm.Engine.DeviceRecoveryFailed += (_, _) =>
            Dispatcher.UIThread.Post(() =>
                _ = MessageWindow.ShowAsync(this, "Audio Device Error",
                    "The audio output device failed and could not be recovered. Please reconnect your audio device and try again."));

#if WINDOWS10_0_19041_0_OR_GREATER
        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle != IntPtr.Zero)
            {
                var session = new Platform.Windows.WindowsMediaSessionService(handle);
                session.StopRequested += (_, _) => RunOnUi(vm.NowPlaying.ResetPlaybackSession);
                _mediaSession = session;
            }
        }
        catch (Exception ex)
        {
            SpectralisLog.Error("SMTC initialization failed.", ex);
        }
#endif

        if (_mediaSession is null)
        {
            return;
        }

        _mediaSession.PlayRequested += (_, _) => RunOnUi(() =>
        {
            if (!vm.NowPlaying.IsPlaying)
            {
                vm.NowPlaying.TogglePlayback();
            }
        });
        _mediaSession.PauseRequested += (_, _) => RunOnUi(() =>
        {
            if (vm.NowPlaying.IsPlaying)
            {
                vm.NowPlaying.TogglePlayback();
            }
        });
        _mediaSession.NextRequested += (_, _) => RunOnUi(() => _ = vm.NowPlaying.PlayNextAsync());
        _mediaSession.PreviousRequested += (_, _) => RunOnUi(() => _ = vm.NowPlaying.PlayPreviousAsync());
        _mediaSession.SeekRequested += (_, position) => RunOnUi(() =>
            vm.NowPlaying.PositionSeconds = position.TotalSeconds);

        vm.NowPlaying.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(NowPlayingViewModel.HasTrack):
                case nameof(NowPlayingViewModel.Title):
                case nameof(NowPlayingViewModel.CoverArtBytes):
                    _mediaSession?.UpdateMetadata(
                        vm.NowPlaying.HasTrack ? vm.NowPlaying.Title : string.Empty,
                        vm.NowPlaying.HasTrack ? vm.NowPlaying.Artist : string.Empty,
                        vm.NowPlaying.Album,
                        vm.NowPlaying.CoverArtBytes);
                    break;

                case nameof(NowPlayingViewModel.IsPlaying):
                case nameof(NowPlayingViewModel.PositionSeconds):
                    _mediaSession?.UpdatePlaybackState(
                        vm.NowPlaying.IsPlaying,
                        TimeSpan.FromSeconds(vm.NowPlaying.PositionSeconds),
                        TimeSpan.FromSeconds(vm.NowPlaying.LengthSeconds));
                    break;
            }
        };
    }

    private static void RunOnUi(Action action) =>
        Dispatcher.UIThread.Post(action);

    private void ApplySavedWindowPlacement()
    {
        if (_placementApplied || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var settings = vm.AppSettings;
        if (!settings.RememberWindowPlacement ||
            settings.WindowWidth < (int)MinWidth ||
            settings.WindowHeight < (int)MinHeight)
        {
            _placementApplied = true;
            return;
        }

        var savedBounds = new PixelRect(
            settings.WindowX,
            settings.WindowY,
            settings.WindowWidth,
            settings.WindowHeight);
        var restoredBounds = ResolveVisibleBounds(savedBounds);
        if (restoredBounds is not null)
        {
            Position = restoredBounds.Value.Position;
            Width = restoredBounds.Value.Width;
            Height = restoredBounds.Value.Height;
        }

        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        _placementApplied = true;
    }

    private PixelRect? ResolveVisibleBounds(PixelRect savedBounds)
    {
        var screens = Screens.All.ToList();
        if (screens.Count == 0)
        {
            return null;
        }

        if (screens.Any(screen => screen.WorkingArea.Intersects(savedBounds)))
        {
            return savedBounds;
        }

        var screen = Screens.ScreenFromPoint(savedBounds.Position)
            ?? Screens.Primary
            ?? screens[0];
        var workArea = screen.WorkingArea;
        var width = Math.Min(savedBounds.Width, workArea.Width);
        var height = Math.Min(savedBounds.Height, workArea.Height);
        var x = Math.Clamp(savedBounds.X, workArea.X, workArea.Right - width);
        var y = Math.Clamp(savedBounds.Y, workArea.Y, workArea.Bottom - height);
        return new PixelRect(x, y, width, height);
    }

    private void SaveWindowPlacement()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var settings = vm.AppSettings;
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            settings.WindowX = Position.X;
            settings.WindowY = Position.Y;
            settings.WindowWidth = (int)ClientSize.Width;
            settings.WindowHeight = (int)ClientSize.Height;
        }

        AppSettingsStore.Save(settings);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var paths = e.Data.GetFiles()?
            .Select(item => item.TryGetLocalPath())
            .Where(path => path is not null)
            .Select(path => path!)
            .ToList();

        if (paths is { Count: > 0 })
        {
            await vm.OpenFilesAsync(paths);
        }
    }

    // ── Resize drag handlers (edge/corner strips from MainWindow.axaml) ─────────

    private void OnResizeNorthWest(object? sender, PointerPressedEventArgs e) => TryBeginResizeDrag(WindowEdge.NorthWest, e);
    private void OnResizeNorth(object? sender, PointerPressedEventArgs e)     => TryBeginResizeDrag(WindowEdge.North, e);
    private void OnResizeNorthEast(object? sender, PointerPressedEventArgs e) => TryBeginResizeDrag(WindowEdge.NorthEast, e);
    private void OnResizeWest(object? sender, PointerPressedEventArgs e)      => TryBeginResizeDrag(WindowEdge.West, e);
    private void OnResizeEast(object? sender, PointerPressedEventArgs e)      => TryBeginResizeDrag(WindowEdge.East, e);
    private void OnResizeSouthWest(object? sender, PointerPressedEventArgs e) => TryBeginResizeDrag(WindowEdge.SouthWest, e);
    private void OnResizeSouth(object? sender, PointerPressedEventArgs e)     => TryBeginResizeDrag(WindowEdge.South, e);
    private void OnResizeSouthEast(object? sender, PointerPressedEventArgs e) => TryBeginResizeDrag(WindowEdge.SouthEast, e);

    private void TryBeginResizeDrag(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (WindowState != WindowState.Maximized &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            BeginResizeDrag(edge, e);
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Control source &&
            (source.FindAncestorOfType<Button>() is not null ||
             source.FindAncestorOfType<MenuItem>() is not null))
        {
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnMinimizeWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private async void OnCloseWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await HandleCloseButtonAsync();
    }

    private void OnToggleSidebar(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.IsSidebarCollapsed = !vm.IsSidebarCollapsed;
        }
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm ||
            IsTextInputSource(e.Source) ||
            IsHandledButtonEvent(e))
        {
            return;
        }

        if (HasOnly(e.KeyModifiers, KeyModifiers.Control | KeyModifiers.Shift))
        {
            switch (e.Key)
            {
                case Key.O:
                    e.Handled = true;
                    await OpenAudioFilesAsync(vm, addToQueue: true);
                    return;

                case Key.L:
                    e.Handled = true;
                    SelectSection(vm, vm.TimingStudio);
                    return;
            }
        }

        if (HasOnly(e.KeyModifiers, KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.Left:
                    e.Handled = true;
                    await vm.NowPlaying.PlayPreviousAsync();
                    return;

                case Key.Right:
                    e.Handled = true;
                    await vm.NowPlaying.PlayNextAsync();
                    return;

                case Key.O:
                    e.Handled = true;
                    await OpenAudioFilesAsync(vm, addToQueue: false);
                    return;

                case Key.B:
                    e.Handled = true;
                    SelectSection(vm, vm.Library);
                    return;

                case Key.P:
                    e.Handled = true;
                    SelectSection(vm, vm.Playlists);
                    return;

                case Key.L:
                    e.Handled = true;
                    await OpenUrlAsync(vm);
                    return;

                case Key.Q:
                    e.Handled = true;
                    // On the Now Playing section Ctrl+Q toggles the queue panel (legacy
                    // parity); from anywhere else it routes there with the panel open.
                    if (ReferenceEquals(vm.CurrentContent, vm.NowPlaying))
                    {
                        vm.NowPlaying.ShowQueue = !vm.NowPlaying.ShowQueue;
                    }
                    else
                    {
                        SelectSection(vm, vm.NowPlaying);
                        vm.NowPlaying.ShowQueue = true;
                    }

                    return;

                case Key.OemComma:
                    e.Handled = true;
                    SelectSection(vm, vm.Settings);
                    return;
            }
        }

        if (HasOnly(e.KeyModifiers, KeyModifiers.Shift))
        {
            switch (e.Key)
            {
                case Key.Left:
                    e.Handled = true;
                    vm.NowPlaying.SeekRelative(-30);
                    return;

                case Key.Right:
                    e.Handled = true;
                    vm.NowPlaying.SeekRelative(30);
                    return;
            }
        }

        if (!HasOnly(e.KeyModifiers, KeyModifiers.None))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.OemComma:
                e.Handled = true;
                vm.NowPlaying.ShowVisualizer = true;
                vm.NowPlaying.ShowYouTubeVideo = false;
                vm.NowPlaying.PreviousVisualizer();
                return;

            case Key.OemPeriod:
            case Key.Decimal:
                e.Handled = true;
                vm.NowPlaying.ShowVisualizer = true;
                vm.NowPlaying.ShowYouTubeVideo = false;
                vm.NowPlaying.NextVisualizer();
                return;

            case Key.Space:
                e.Handled = true;
                vm.NowPlaying.TogglePlayback();
                return;

            case Key.Escape:
                e.Handled = true;
                if (!ReferenceEquals(vm.SelectedSection.Content, vm.NowPlaying))
                {
                    SelectSection(vm, vm.NowPlaying);
                }
                else
                {
                    vm.NowPlaying.ResetPlaybackSession();
                }
                return;

            case Key.Left:
                e.Handled = true;
                vm.NowPlaying.SeekRelative(-5);
                return;

            case Key.Right:
                e.Handled = true;
                vm.NowPlaying.SeekRelative(5);
                return;

            case Key.Up:
                e.Handled = true;
                vm.NowPlaying.AdjustVolume(5);
                return;

            case Key.Down:
                e.Handled = true;
                vm.NowPlaying.AdjustVolume(-5);
                return;

            case Key.M:
                e.Handled = true;
                vm.NowPlaying.ToggleMute();
                return;
        }
    }

    private async Task OpenUrlAsync(MainWindowViewModel vm)
    {
        var url = await OpenUrlWindow.PromptAsync(this);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        vm.SelectedSection = vm.Sections[0];
        await vm.NowPlaying.LoadUrlAsync(url);
    }

    private async Task OpenAudioFilesAsync(MainWindowViewModel vm, bool addToQueue)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = addToQueue ? "Add files to queue" : "Open audio file",
            AllowMultiple = true,
            FileTypeFilter = BuildOpenFileFilters(addToQueue),
        });

        var paths = files
            .Select(file => file.TryGetLocalPath())
            .Where(path => path is not null && File.Exists(path))
            .Select(path => path!)
            .ToList();

        if (paths.Count == 0)
        {
            return;
        }

        if (addToQueue)
        {
            var audioPaths = paths
                .Where(SupportedAudioFormats.IsSupportedExtension)
                .ToList();
            if (audioPaths.Count > 0)
            {
                SelectSection(vm, vm.NowPlaying);
                await vm.NowPlaying.QueueFilesAsync(audioPaths, vm.AppSettings.AutoPlayOnOpen);
            }

            return;
        }

        await vm.OpenFilesAsync(paths);
    }

    private static IReadOnlyList<FilePickerFileType> BuildOpenFileFilters(bool addToQueue)
    {
        var filters = new List<FilePickerFileType>
        {
            new("Audio files")
            {
                Patterns = SupportedAudioFormats.Extensions.Select(ext => "*" + ext).ToArray(),
            },
        };

        if (!addToQueue)
        {
            filters.Add(new FilePickerFileType("Spectralis packages")
            {
                Patterns = ["*.spectralis", "*.spectral"],
            });
        }

        filters.Add(FilePickerFileTypes.All);
        return filters;
    }

    private async void OnMenuOpenAudio(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await OpenAudioFilesAsync(vm, addToQueue: false);
        }
    }

    private async void OnMenuAddToQueue(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await OpenAudioFilesAsync(vm, addToQueue: true);
        }
    }

    private async void OnMenuOpenUrl(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await OpenUrlAsync(vm);
        }
    }

    private void OnMenuSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectSection(vm.Settings);
        }
    }

    private void OnMenuExit(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => RequestAppExit();

    private void OnMenuNowPlaying(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectSection(vm.NowPlaying);
        }
    }

    private void OnMenuLibrary(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectSection(vm.Library);
        }
    }

    private void OnMenuPlaylists(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectSection(vm.Playlists);
        }
    }

    private void OnMenuTimingStudio(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectSection(vm.TimingStudio);
        }
    }

    private async void OnMenuPlayPause(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (vm.NowPlaying.HasTrack)
        {
            vm.NowPlaying.TogglePlayback();
        }
        else
        {
            await OpenAudioFilesAsync(vm, addToQueue: false);
        }
    }

    private void OnMenuToggleMute(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.NowPlaying.ToggleMute();
        }
    }

    private async void OnMenuOpenPlaylist(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open M3U playlist",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("M3U playlists") { Patterns = ["*.m3u", "*.m3u8"] },
                FilePickerFileTypes.All,
            ],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null)
        {
            return;
        }

        var paths = Spectralis.Core.Playlists.M3uParser.Import(path).Where(File.Exists).ToList();
        if (paths.Count > 0)
        {
            vm.SelectSection(vm.NowPlaying);
            await vm.NowPlaying.PlayQueueAsync(paths, 0);
        }
    }

    private async void OnMenuSaveQueueAsPlaylist(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.NowPlaying.Queue.IsEmpty)
        {
            return;
        }

        var name = await NameInputWindow.PromptAsync(this, "Save Queue as Playlist", "Playlist name:", "Queue");
        if (!string.IsNullOrWhiteSpace(name))
        {
            vm.Playlists.CreatePlaylist(name, vm.NowPlaying.Queue.Items);
        }
    }

    private EffectsChainWindow? _effectsWindow;

    private void OnMenuEffectsChain(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (_effectsWindow is { IsVisible: true })
        {
            _effectsWindow.Activate();
            return;
        }

        _effectsWindow = new EffectsChainWindow
        {
            DataContext = new EffectsChainViewModel(vm.EffectChain),
        };
        _effectsWindow.Closed += (_, _) => _effectsWindow = null;
        _effectsWindow.Show(this);
    }

    private ScriptedVisualizerManagerWindow? _scriptedVizWindow;

    private void OnMenuScriptedVisualizers(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (_scriptedVizWindow is { IsVisible: true }) { _scriptedVizWindow.Activate(); return; }
        _scriptedVizWindow = new ScriptedVisualizerManagerWindow(def =>
        {
            vm.NowPlaying.ScriptedVisualizerOverride =
                def is not null
                    ? new Spectralis.Core.Visualizers.Scripting.ScriptVisualizerRenderer(def)
                    : null;
            vm.NowPlaying.RefreshVisualizerOptions();
        });
        _scriptedVizWindow.Closed += (_, _) =>
        {
            vm.NowPlaying.RefreshVisualizerOptions();
            _scriptedVizWindow = null;
        };
        _scriptedVizWindow.Show(this);
    }

    private KaraokeWindow? _karaokeWindow;

    private void OnMenuKaraokeMode(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (_karaokeWindow is { IsVisible: true })
        {
            _karaokeWindow.Activate();
            return;
        }

        _karaokeWindow = new KaraokeWindow(
            vm.Engine,
            vm.EffectChain,
            vm.NowPlaying.TogglePlayback,
            () => vm.NowPlaying.CurrentLyrics);
        _karaokeWindow.Closed += (_, _) => _karaokeWindow = null;
        _karaokeWindow.Show(this);
    }

    private MetronomeWindow? _metronomeWindow;

    private void OnMenuMetronome(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_metronomeWindow is { IsVisible: true })
        {
            _metronomeWindow.Activate();
            return;
        }

        // Seed the metronome with the current track's analyzed BPM when known.
        var initialBpm = DataContext is MainWindowViewModel vm && vm.NowPlaying.HasBeatGrid
            ? (float)vm.NowPlaying.BeatGridBpm
            : 120f;
        _metronomeWindow = new MetronomeWindow(initialBpm);
        _metronomeWindow.Closed += (_, _) => _metronomeWindow = null;
        _metronomeWindow.Show(this);
    }

    private void OnMenuAnalyzeLibrary(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Library.StartAnalysis();
            vm.SelectSection(vm.Library);
        }
    }

    private async void OnMenuListeningStats(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await new StatsWindow().ShowDialog(this);

    private SongWarsWindow? _songWarsWindow;

    private void OnMenuSongWars(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (_songWarsWindow is { IsVisible: true })
        {
            _songWarsWindow.Activate();
            return;
        }

        if (vm.NowPlaying.ShowSongWarsPanel)
        {
            // Already docked — undock to bring up the window
            SongWarsUndock(vm);
            return;
        }

        OpenSongWarsWindow(vm);
    }

    private void OpenSongWarsWindow(MainWindowViewModel vm)
    {
        _songWarsWindow = new SongWarsWindow();
        _songWarsWindow.RequestPlay = path => _ = vm.NowPlaying.PlayQueueAsync([path], 0);
        _songWarsWindow.RequestDock = () => SongWarsDock(vm);
        _songWarsWindow.Closed += (_, _) =>
        {
            _songWarsWindow = null;
            vm.ObsOverlay.GetActiveTournament = null;
            vm.NowPlaying.SongWarsPopOutRequested = null;
        };
        vm.ObsOverlay.GetActiveTournament = () =>
            _songWarsWindow?.CurrentTournament ?? vm.NowPlaying.SongWarsSession?.Tournament;
        _songWarsWindow.Show(this);
    }

    private void SongWarsDock(MainWindowViewModel vm)
    {
        if (_songWarsWindow is null) return;
        vm.NowPlaying.SongWarsSession = _songWarsWindow.CurrentSession;
        vm.NowPlaying.NotifySongWarsChanged();
        vm.NowPlaying.ShowSongWarsPanel = vm.NowPlaying.SongWarsHasSession;
        vm.NowPlaying.SongWarsPopOutRequested = () => SongWarsUndock(vm);
        _songWarsWindow.Hide();
    }

    private void SongWarsUndock(MainWindowViewModel vm)
    {
        vm.NowPlaying.ShowSongWarsPanel = false;
        vm.NowPlaying.SongWarsPopOutRequested = null;
        if (_songWarsWindow is not null)
        {
            _songWarsWindow.Show(this);
        }
        else
        {
            OpenSongWarsWindow(vm);
        }
    }

    private async void OnMenuScrobblingSettings(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await ScrobblingSettingsWindow.ShowAsync(this, vm.AppSettings);
            // A newly linked account can submit anything queued while offline.
            _ = vm.Scrobbling.DrainQueueAsync();
        }
    }

    private void OnMenuCheckUpdates(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Settings.CheckForUpdates();
            vm.SelectSection(vm.Settings);
        }
    }

    private async void OnMenuTermsOfService(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await new LegalDocumentWindow(LegalDocumentKind.TermsOfService).ShowDialog(this);

    private async void OnMenuPrivacyPolicy(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await new LegalDocumentWindow(LegalDocumentKind.PrivacyPolicy).ShowDialog(this);

    private void OnMenuExportVideo(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var track = vm.Engine.CurrentTrack;
        if (track is null || string.IsNullOrWhiteSpace(track.SourcePath) || !File.Exists(track.SourcePath))
        {
            _ = MessageWindow.ShowAsync(this, "Export Video",
                "Open a local audio file to export a visualizer video.");
            return;
        }

        new VideoExportWindow(
            track.SourcePath,
            vm.NowPlaying.Title,
            vm.NowPlaying.Artist,
            vm.NowPlaying.CoverArtBytes,
            vm.NowPlaying.SelectedVisualizerMode,
            isExporting => vm.NowPlaying.IsExporting = isExporting)
            .Show(this);
    }

    private async void OnMenuSetAsDefault(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows())
        {
            await MessageWindow.ShowAsync(this, "Set as Default App",
                "Default app registration is only supported on Windows.");
            return;
        }

        try
        {
            var registrar = new Spectralis.App.Platform.Windows.WindowsProtocolRegistrar();
            registrar.RegisterProtocol();
            registrar.RegisterFileAssociations(
                Spectralis.Core.Common.SupportedAudioFormats.Extensions
                    .Concat([".spectralis", ".spectral"]).ToArray());
        }
        catch (Exception ex)
        {
            await MessageWindow.ShowAsync(this, "Set as Default App",
                $"Registration failed: {ex.Message}");
            return;
        }

#if WINDOWS10_0_19041_0_OR_GREATER
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:defaultapps?registeredAppUser=Spectralis",
                UseShellExecute = true,
            });
        }
        catch { }
#endif
    }

    private void OnMenuRedeemVisualizer(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var win = new RedeemVisualizerWindow();
        win.Closed += (_, _) => vm.NowPlaying.RefreshVisualizerOptions();
        win.Show(this);
    }

    private async void OnMenuClearRedeemedVisualizers(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var store = new Spectralis.Core.Visualizers.Installed.InstalledVisualizerStore();
        var count = store.Count();
        if (count == 0)
        {
            await MessageWindow.ShowAsync(this, "Clear Redeemed Visualizers",
                "No redeemed visualizers are installed on this device.");
            return;
        }

        var confirmed = await ConfirmWindow.ShowAsync(this,
            "Clear Redeemed Visualizers",
            $"Remove all {count} installed visualizer{(count == 1 ? "" : "s")} from this device?",
            "Clear", "Cancel");
        if (!confirmed) return;

        try
        {
            store.ClearAll();
            if (DataContext is MainWindowViewModel vm)
                vm.NowPlaying.RefreshVisualizerOptions();
            await MessageWindow.ShowAsync(this, "Clear Redeemed Visualizers",
                "All redeemed visualizers have been removed.");
        }
        catch (Exception ex)
        {
            await MessageWindow.ShowAsync(this, "Clear Redeemed Visualizers",
                $"Could not clear redeemed visualizers.\n\n{ex.Message}");
        }
    }

    private async void OnMenuClearCachedAlbumState(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var confirmed = await ConfirmWindow.ShowAsync(this,
            "Clear Cached Album State",
            "Clear cached album progress, unlock state, bookmarks, and current positions from this device?",
            "Clear", "Cancel");
        if (!confirmed) return;

        var albumWorldDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis", "AlbumWorlds");
        try
        {
            if (Directory.Exists(albumWorldDir))
                Directory.Delete(albumWorldDir, recursive: true);
            await MessageWindow.ShowAsync(this, "Clear Cached Album State", "Cached album state has been cleared.");
        }
        catch (Exception ex)
        {
            await MessageWindow.ShowAsync(this, "Clear Cached Album State",
                $"Could not clear cached album state.\n\n{ex.Message}");
        }
    }

    private async void OnMenuAbout(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        await new AboutWindow().ShowDialog(this);

    private void OnMenuVisitWebsite(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => OpenDeltavDevsSite();

    private void OnBrandLinkPressed(object? sender, PointerPressedEventArgs e) => OpenDeltavDevsSite();

    private async void OnClipboardMonitorTick(object? sender, EventArgs e)
    {
        if (_checkingClipboard ||
            DataContext is not MainWindowViewModel vm ||
            !vm.AppSettings.EnableClipboardUrlMonitoring)
        {
            return;
        }

        vm.DismissClipboardToastIfExpired(TimeSpan.FromSeconds(30));

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        _checkingClipboard = true;
        try
        {
            var text = await clipboard.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text) ||
                string.Equals(text, _lastClipboardText, StringComparison.Ordinal))
            {
                return;
            }

            _lastClipboardText = text;
            foreach (var url in EnumerateClipboardUrls(text))
            {
                if (!OpenUrlService.MightBeUrl(url))
                {
                    continue;
                }

                var historyKey = url.Trim();
                if (_clipboardUrlHistory.Contains(historyKey))
                {
                    continue;
                }

                // Quick metadata-only resolve (name + art, no audio download) before
                // suggesting playback, so a dead/private/unsupported link never gets
                // suggested. The full resolve (and, for yt-dlp-backed services, the
                // download) happens lazily when Play is actually clicked.
                RemoteAudioResolveResult resolved;
                try
                {
                    resolved = await _clipboardUrlResolver.ResolveAsync(historyKey, CancellationToken.None, quickOnly: true);
                }
                catch (Exception ex)
                {
                    AddClipboardHistory(historyKey);
                    SpectralisLog.Warn($"Clipboard link did not resolve to a playable track: {ex.Message}");
                    continue;
                }

                AddClipboardHistory(historyKey);

                var trackKey = BuildTrackIdentityKey(resolved.Title, resolved.Artist) ?? historyKey;
                if (_toastedTrackHistory.Contains(trackKey))
                {
                    continue;
                }
                AddToastedTrackHistory(trackKey);

                var artwork = await OpenUrlService.TryFetchArtworkBytesAsync(resolved.ArtworkUrl, CancellationToken.None);
                vm.ShowClipboardUrlToast(resolved.SourceUrl, resolved, artwork);
                return;
            }
        }
        catch (Exception ex)
        {
            SpectralisLog.Warn($"Clipboard monitor skipped an update: {ex.Message}");
        }
        finally
        {
            _checkingClipboard = false;
        }
    }

    private async void OnPlayClipboardToast(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.PlayClipboardToastUrlAsync();
        }
    }

    private void OnDismissClipboardToast(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.DismissClipboardToast();
        }
    }

    private async Task<bool> PromptContentWarningAsync(string[] tags, string trackName) =>
        await ContentWarningWindow.PromptAsync(this, tags, trackName);

    private async Task<bool> PromptTrustCreatorAsync(CapsuleTrustContext context) =>
        await CapsuleTrustWindow.ShowAsync(this, context);

    private static IEnumerable<string> EnumerateClipboardUrls(string text)
    {
        foreach (Match match in ClipboardUrlRegex.Matches(text))
        {
            var url = TrimClipboardUrl(match.Value);
            if (!string.IsNullOrWhiteSpace(url))
            {
                yield return url;
            }
        }
    }

    private static string TrimClipboardUrl(string value) =>
        value.Trim().TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '"', '\'');

    private void AddClipboardHistory(string key)
    {
        if (!_clipboardUrlHistory.Add(key))
        {
            return;
        }

        _clipboardUrlHistoryOrder.Enqueue(key);
        while (_clipboardUrlHistoryOrder.Count > ClipboardUrlHistoryLimit)
        {
            _clipboardUrlHistory.Remove(_clipboardUrlHistoryOrder.Dequeue());
        }
    }

    /// <summary>Identifies a resolved track by title+artist so the same song doesn't toast
    /// twice in a row from two different links (e.g. a YouTube link then its Spotify link).</summary>
    private static string? BuildTrackIdentityKey(string? title, string? artist)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return $"{artist?.Trim().ToLowerInvariant()}|{title.Trim().ToLowerInvariant()}";
    }

    private void AddToastedTrackHistory(string key)
    {
        if (!_toastedTrackHistory.Add(key))
        {
            return;
        }

        _toastedTrackHistoryOrder.Enqueue(key);
        while (_toastedTrackHistoryOrder.Count > ClipboardUrlHistoryLimit)
        {
            _toastedTrackHistory.Remove(_toastedTrackHistoryOrder.Dequeue());
        }
    }

    private static void OpenDeltavDevsSite()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://deltavdevs.com",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            SpectralisLog.Error("Failed to open DeltaVDevs website.", ex);
        }
    }

    private static void SelectSection(MainWindowViewModel vm, ViewModelBase content)
    {
        var section = vm.Sections.FirstOrDefault(section => ReferenceEquals(section.Content, content));
        if (section is not null)
        {
            vm.SelectedSection = section;
        }
    }

    private static bool HasOnly(KeyModifiers actual, KeyModifiers expected) =>
        (actual & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt)) == expected;

    private static bool IsHandledButtonEvent(KeyEventArgs e) =>
        e.Handled &&
        e.Source is Control control &&
        (control is Button || control.FindAncestorOfType<Button>() is not null);

    private static bool IsTextInputSource(object? source)
    {
        if (source is not Control control)
        {
            return false;
        }

        return control is TextBox or ComboBox or NumericUpDown ||
            control.FindAncestorOfType<TextBox>() is not null ||
            control.FindAncestorOfType<ComboBox>() is not null ||
            control.FindAncestorOfType<NumericUpDown>() is not null;
    }
}
