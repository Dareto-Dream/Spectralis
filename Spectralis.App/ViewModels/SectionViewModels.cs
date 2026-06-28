using System.Reactive;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Diagnostics;
using ReactiveUI;
using Spectralis.App.Services;
using Spectralis.App.Platform.Windows;
using Spectralis.Core.Audio.Midi;
using Spectralis.Core.Capsule;
using Spectralis.Core.Common;
using Spectralis.Core.Embedded;
using Spectralis.Core.Metadata;
using Spectralis.Core.Visualizers;
using Spectralis.Core.Integrations.Spotify;
using Spectralis.Core.SharedPlay;

namespace Spectralis.App.ViewModels;

// Section ViewModels start as routed placeholders; each gains its real state as
// its feature lands. They stay in separate-but-small form here until they grow.

public sealed class SharedPlayViewModel : ViewModelBase, IDisposable
{
    private readonly SharedPlaySessionController _controller = new();
    private string _statusText = "No active room";
    private string _joinUrl = string.Empty;
    private string _roomCode = string.Empty;
    private bool _isHosting;
    private string _lastError = string.Empty;

    public SharedPlayViewModel()
    {
        _controller.StatusChanged += OnStatusChanged;
        HostCommand     = ReactiveCommand.CreateFromTask(HostAsync);
        StopCommand     = ReactiveCommand.Create(Stop);
        CopyLinkCommand = ReactiveCommand.Create(CopyLink, this.WhenAnyValue(x => x.IsHosting));
    }

    public ReactiveCommand<Unit, Unit> HostCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyLinkCommand { get; }

    public event Action<string>? CopyToClipboardRequested;

    public bool IsHosting
    {
        get => _isHosting;
        private set => this.RaiseAndSetIfChanged(ref _isHosting, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public string JoinUrl
    {
        get => _joinUrl;
        private set => this.RaiseAndSetIfChanged(ref _joinUrl, value);
    }

    public string RoomCode
    {
        get => _roomCode;
        private set => this.RaiseAndSetIfChanged(ref _roomCode, value);
    }

    public string LastError
    {
        get => _lastError;
        private set => this.RaiseAndSetIfChanged(ref _lastError, value);
    }

    public void ApplySettings(AppSettings settings)
    {
        _controller.ApplySettings(
            settings.SharedPlayEnabled,
            settings.SharedPlayCdnBaseUrl,
            settings.SharedPlayLiveChannelEnabled,
            settings.SharedPlayLiveChannelId,
            settings.SharedPlayLiveChannelOwnerToken,
            settings.SharedPlayLiveChannelDisplayName);
    }

    public void NotifyPlayback(TrackInfo? track, bool isPlaying, double positionSeconds, double durationSeconds, string reason = "tick")
    {
        if (!_isHosting) return;
        _controller.NotifyPlaybackChanged(track, isPlaying, positionSeconds, durationSeconds, reason);
    }

    private Task HostAsync()
    {
        _controller.ApplySettings(
            enableSharedPlay: true,
            cdnBaseUrl: null,
            enableLiveChannel: false,
            channelId: string.Empty,
            channelOwnerToken: string.Empty,
            channelDisplayName: "Spectralis Listener");
        OnStatusChanged(null, EventArgs.Empty);
        return Task.CompletedTask;
    }

    private void Stop()
    {
        _controller.ClearActiveSession();
        _controller.ApplySettings(false, null, false, string.Empty, string.Empty, string.Empty);
        OnStatusChanged(null, EventArgs.Empty);
    }

    private void CopyLink()
    {
        if (!string.IsNullOrEmpty(_joinUrl))
            CopyToClipboardRequested?.Invoke(_joinUrl);
    }

    private void OnStatusChanged(object? sender, EventArgs _)
    {
        var snap = _controller.Snapshot;
        IsHosting = snap.IsEnabled && !string.IsNullOrEmpty(snap.RoomCode);
        JoinUrl = snap.JoinUrl ?? string.Empty;
        RoomCode = snap.DisplayCode ?? snap.RoomCode ?? string.Empty;
        LastError = snap.LastError ?? string.Empty;
        StatusText = BuildStatusText(snap);
    }

    private static string BuildStatusText(SharedPlaySessionSnapshot snap)
    {
        if (!snap.IsEnabled) return "Shared Play is disabled.";
        if (!string.IsNullOrEmpty(snap.LastError)) return $"Error: {snap.LastError}";
        if (snap.IsUploading) return "Uploading track...";
        if (!string.IsNullOrEmpty(snap.DisplayCode)) return $"Room: {snap.DisplayCode}";
        if (!string.IsNullOrEmpty(snap.RoomCode)) return $"Room: {SharedPlayDefaults.DisplayRoomCode(snap.RoomCode)}";
        return "Ready to host.";
    }

    public void Dispose() => _controller.Dispose();
}

public sealed class CapsuleTrackViewModel
{
    public CapsuleTrackViewModel(string title, string artist, string durationText)
    {
        Title = title;
        Artist = artist;
        DurationText = durationText;
    }

    public string Title { get; }
    public string Artist { get; }
    public string DurationText { get; }
}

public sealed class CapsulesViewModel : ViewModelBase
{
    private readonly Func<string, TrackInfo, bool, Task> _loadPreparedTrack;
    private bool _hasPackage;
    private string _title = "No capsules opened";
    private string _subtitle = "Open a signed .spectralis capsule or .spectral album world to see it here.";
    private string _summary = string.Empty;
    private string _status = "Ready for .spectralis and .spectral files.";
    private string _formatLabel = string.Empty;
    private string _creatorText = string.Empty;
    private string _fingerprintText = string.Empty;
    private string _capabilitiesText = string.Empty;
    private string _entryCountText = string.Empty;

    public CapsulesViewModel(Func<string, TrackInfo, bool, Task> loadPreparedTrack)
    {
        _loadPreparedTrack = loadPreparedTrack;
    }

    public Func<CapsuleTrustContext, Task<bool>> TrustCreatorPrompt { get; set; } =
        _ => Task.FromResult(false);

    public ObservableCollection<CapsuleTrackViewModel> Tracks { get; } = [];

    public bool HasPackage
    {
        get => _hasPackage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _hasPackage, value);
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsEmpty => !HasPackage;

    public string Title
    {
        get => _title;
        private set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => this.RaiseAndSetIfChanged(ref _subtitle, value);
    }

    public string Summary
    {
        get => _summary;
        private set
        {
            this.RaiseAndSetIfChanged(ref _summary, value);
            this.RaisePropertyChanged(nameof(HasSummary));
        }
    }

    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public string FormatLabel
    {
        get => _formatLabel;
        private set => this.RaiseAndSetIfChanged(ref _formatLabel, value);
    }

    public string CreatorText
    {
        get => _creatorText;
        private set => this.RaiseAndSetIfChanged(ref _creatorText, value);
    }

    public string FingerprintText
    {
        get => _fingerprintText;
        private set => this.RaiseAndSetIfChanged(ref _fingerprintText, value);
    }

    public string CapabilitiesText
    {
        get => _capabilitiesText;
        private set => this.RaiseAndSetIfChanged(ref _capabilitiesText, value);
    }

    public string EntryCountText
    {
        get => _entryCountText;
        private set => this.RaiseAndSetIfChanged(ref _entryCountText, value);
    }

    public void Clear()
    {
        HasPackage = false;
        Title = "No capsules opened";
        Subtitle = "Open a signed .spectralis capsule or .spectral album world to see it here.";
        Summary = string.Empty;
        Status = "Ready for .spectralis and .spectral files.";
        FormatLabel = string.Empty;
        CreatorText = string.Empty;
        FingerprintText = string.Empty;
        CapabilitiesText = string.Empty;
        EntryCountText = string.Empty;
        Tracks.Clear();
    }

    public async Task OpenFilesAsync(IReadOnlyList<string> paths, bool startPlayback)
    {
        foreach (var path in paths)
        {
            if (IsSpectralisCapsule(path))
            {
                await OpenSpectralisAsync(path, startPlayback);
                continue;
            }

            if (IsSpectralAlbum(path))
            {
                OpenSpectralAlbum(path);
            }
        }
    }

    public static bool IsPackage(string path) =>
        IsSpectralisCapsule(path) || IsSpectralAlbum(path);

    private static bool IsSpectralisCapsule(string path) =>
        string.Equals(Path.GetExtension(path), ".spectralis", StringComparison.OrdinalIgnoreCase);

    private static bool IsSpectralAlbum(string path) =>
        string.Equals(Path.GetExtension(path), ".spectral", StringComparison.OrdinalIgnoreCase);

    private async Task OpenSpectralisAsync(string path, bool startPlayback)
    {
        Status = "Opening signed capsule...";
        Tracks.Clear();

        try
        {
            var trustStore = new CreatorTrustStore();
            trustStore.Load();
            using var runtime = new CapsuleTrustRuntime(trustStore);
            var result = await runtime.OpenAsync(path, TrustCreatorPrompt, CancellationToken.None);
            if (!result.IsSuccess || result.Package is null)
            {
                HasPackage = false;
                Status = result.ErrorMessage ?? $"Could not open capsule: {result.Status}.";
                return;
            }

            var package = result.Package;
            var manifest = package.Manifest;
            ApplySingleCapsule(package, result.KeyMetadata);

            if (!string.IsNullOrWhiteSpace(manifest.Audio.Entry))
            {
                var tempPath = ExtractAudio(package);
                var metadata = ReadMetadataOrFallback(tempPath);
                var packageModules = EmbeddedModuleReader.ReadFromPackageVisualizers(
                    manifest.Visualizers,
                    package.TryReadEntry);
                var artwork = TryReadArtwork(package, manifest.Story.ImageEntry)
                    ?? manifest.Assets.Images.Select(package.TryReadEntry).FirstOrDefault(bytes => bytes is not null);
                // If the capsule has story pages and no other HTML surface, synthesize a visual-novel pager.
                var storyHtml = (packageModules.Html is null && metadata.EmbeddedHtml is null)
                    ? CapsuleStoryRenderer.TryToHtmlContext(manifest.Story, package.TryReadEntry)
                    : null;

                var trackInfo = metadata with
                {
                    SourcePath = tempPath,
                    Title = FirstNonEmpty(manifest.Title, metadata.Title, Path.GetFileNameWithoutExtension(path)),
                    Artist = FirstNonEmpty(manifest.Artist, metadata.Artist),
                    Album = FirstNonEmpty(manifest.Release.Album, metadata.Album),
                    Year = manifest.Release.Year > 0 ? (uint)manifest.Release.Year : metadata.Year,
                    Duration = manifest.Audio.DurationSeconds > 0
                        ? TimeSpan.FromSeconds(manifest.Audio.DurationSeconds)
                        : metadata.Duration,
                    CoverArt = artwork ?? metadata.CoverArt,
                    CoverArtMimeType = artwork is null ? metadata.CoverArtMimeType : GuessMimeType(manifest.Story.ImageEntry),
                    EmbeddedVisualizer = packageModules.Visualizer ?? metadata.EmbeddedVisualizer,
                    EmbeddedHtml = packageModules.Html ?? storyHtml ?? metadata.EmbeddedHtml,
                    EmbeddedMarkdown = packageModules.Markdown ?? metadata.EmbeddedMarkdown,
                    EmbeddedVideo = packageModules.Video ?? metadata.EmbeddedVideo,
                };

                await _loadPreparedTrack(tempPath, trackInfo, startPlayback);
                var embeddedSummary = CreateEmbeddedSummary(trackInfo);
                Status = startPlayback
                    ? $"Opened and started capsule audio{embeddedSummary}."
                    : $"Opened capsule audio{embeddedSummary}.";
            }
            else
            {
                Status = "Opened capsule metadata. No audio entry was declared.";
            }
        }
        catch (Exception ex)
        {
            HasPackage = false;
            Status = $"Capsule open failed: {ex.Message}";
        }
    }

    private void OpenSpectralAlbum(string path)
    {
        Status = "Opening signed album world...";
        Tracks.Clear();

        try
        {
            using var package = AlbumCapsuleReader.Read(path);
            var manifest = package.Manifest;
            Title = FirstNonEmpty(manifest.Title, Path.GetFileNameWithoutExtension(path));
            Subtitle = FirstNonEmpty(manifest.Artist, manifest.Release.Album, "Spectral album world");
            Summary = manifest.Story.Summary;
            FormatLabel = ".spectral album world";
            CreatorText = string.IsNullOrWhiteSpace(manifest.Artist) ? "Unknown artist" : manifest.Artist;
            FingerprintText = package.Fingerprint;
            CapabilitiesText = manifest.Capabilities.Count == 0
                ? "No deep capabilities requested"
                : string.Join(", ", manifest.Capabilities);
            EntryCountText = $"{package.EntryNames().Count} package entries";

            var embeddedTrackCount = 0;
            foreach (var track in manifest.Tracks)
            {
                var modules = EmbeddedModuleReader.ReadFromPackageVisualizers(
                    track.Visualizers,
                    package.TryReadEntry);
                if (modules.HasAny)
                {
                    embeddedTrackCount++;
                }

                Tracks.Add(new CapsuleTrackViewModel(
                    FirstNonEmpty(track.Title, track.Id, "Untitled track"),
                    FirstNonEmpty(track.Artist, manifest.Artist),
                    FormatDuration(track.Audio.DurationSeconds)));
            }

            HasPackage = true;
            Status = embeddedTrackCount > 0
                ? $"Opened album world metadata with embedded visualizers on {embeddedTrackCount} track(s). Album-world playback migration is still pending."
                : "Opened album world metadata. Album-world playback migration is still pending.";
        }
        catch (Exception ex)
        {
            HasPackage = false;
            Status = $"Album world open failed: {ex.Message}";
        }
    }

    private void ApplySingleCapsule(CapsulePackage package, CreatorKeyMetadata? creator)
    {
        var manifest = package.Manifest;
        Title = FirstNonEmpty(manifest.Title, Path.GetFileNameWithoutExtension(package.FilePath));
        Subtitle = FirstNonEmpty(manifest.Artist, manifest.Release.Album, "Spectralis capsule");
        Summary = FirstNonEmpty(manifest.Story.Summary, manifest.Story.Backstory);
        FormatLabel = ".spectralis capsule";
        CreatorText = creator is null || string.IsNullOrWhiteSpace(creator.DisplayName)
            ? manifest.Artist
            : creator.DisplayName;
        FingerprintText = package.Fingerprint;
        CapabilitiesText = manifest.Capabilities.Count == 0
            ? "No deep capabilities requested"
            : string.Join(", ", manifest.Capabilities);
        EntryCountText = $"{package.EntryNames().Count} package entries";
        Tracks.Add(new CapsuleTrackViewModel(
            FirstNonEmpty(manifest.Title, "Untitled capsule track"),
            FirstNonEmpty(manifest.Artist, CreatorText),
            FormatDuration(manifest.Audio.DurationSeconds)));
        HasPackage = true;
    }

    private static string ExtractAudio(CapsulePackage package)
    {
        var entry = package.Manifest.Audio.Entry;
        var audioBytes = package.TryReadEntry(entry)
            ?? throw new InvalidDataException($"Capsule audio entry '{entry}' was not found.");

        if (!string.IsNullOrWhiteSpace(package.Manifest.Audio.Sha256))
        {
            var actualSha = Convert.ToHexString(SHA256.HashData(audioBytes)).ToLowerInvariant();
            if (!string.Equals(actualSha, package.Manifest.Audio.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Capsule audio SHA-256 does not match the manifest.");
            }
        }

        var extension = Path.GetExtension(entry);
        if (string.IsNullOrWhiteSpace(extension) || !SupportedAudioFormats.IsSupportedExtension(extension))
        {
            extension = ".mp3";
        }

        var directory = Path.Combine(Path.GetTempPath(), "spectralis", "capsules");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $"spectralis-capsule-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(tempPath, audioBytes);
        return tempPath;
    }

    private static TrackInfo ReadMetadataOrFallback(string path)
    {
        try
        {
            return TrackMetadataReader.Read(path);
        }
        catch
        {
            return new TrackInfo
            {
                SourcePath = path,
                FileSizeBytes = new FileInfo(path).Length,
                FormatName = Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
            };
        }
    }

    private static byte[]? TryReadArtwork(CapsulePackage package, string? entry) =>
        string.IsNullOrWhiteSpace(entry) ? null : package.TryReadEntry(entry);

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

    private static string FormatDuration(double seconds) =>
        seconds > 0
            ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss")
            : string.Empty;

    private static string CreateEmbeddedSummary(TrackInfo track)
    {
        var parts = new List<string>();
        if (track.EmbeddedHtml is not null)
        {
            parts.Add("HTML");
        }

        if (track.EmbeddedVisualizer is not null)
        {
            parts.Add("WASM");
        }

        if (track.EmbeddedMarkdown is not null)
        {
            parts.Add("Markdown");
        }

        if (track.EmbeddedVideo is not null)
        {
            parts.Add("video");
        }

        return parts.Count == 0
            ? string.Empty
            : $" with embedded {string.Join(", ", parts)}";
    }

    private static string? GuessMimeType(string? entry) =>
        Path.GetExtension(entry ?? string.Empty).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "image/jpeg",
        };
}

public sealed class ObsDesignerItem : ViewModelBase
{
    private double _x, _y, _w, _h;

    public ObsDesignerItem(Spectralis.Core.Integrations.Obs.ObsLayoutWidget source)
    {
        Source = source;
        _x = source.X;
        _y = source.Y;
        _w = source.W;
        _h = source.H;
    }

    public Spectralis.Core.Integrations.Obs.ObsLayoutWidget Source { get; }
    public string Label => Source.Type;

    public double X { get => _x; set => this.RaiseAndSetIfChanged(ref _x, value); }
    public double Y { get => _y; set => this.RaiseAndSetIfChanged(ref _y, value); }
    public double W { get => _w; set => this.RaiseAndSetIfChanged(ref _w, Math.Max(0.02, value)); }
    public double H { get => _h; set => this.RaiseAndSetIfChanged(ref _h, Math.Max(0.02, value)); }

    public void CommitToSource()
    {
        Source.X = X;
        Source.Y = Y;
        Source.W = W;
        Source.H = H;
    }
}

public sealed class ObsOverlayOption
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsDefault => string.IsNullOrWhiteSpace(Id);
}

public sealed class ObsEditorViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly Action<bool>? _setObsOverlayEnabled;
    private readonly Action? _restartObsOverlay;
    private readonly Action<Spectralis.Core.Integrations.Obs.ObsLayout>? _setObsLayout;
    private readonly Action<string, Spectralis.Core.Integrations.Obs.ObsLayout>? _setNamedLayout;
    private readonly Action<string>? _removeNamedLayout;
    private string _obsOverlayUrl = string.Empty;
    private string _obsStatus = string.Empty;
    private string _customObsJson;
    // Overlay management
    private ObsOverlayOption? _selectedOverlay;
    private bool _isAddingOverlay;
    private string _newOverlayName = string.Empty;
    // Preset management
    private Spectralis.Core.Integrations.Obs.ObsPreset? _selectedBuiltInPreset;
    private Spectralis.Core.Integrations.Obs.ObsPreset? _selectedUserPreset;
    private string _newPresetName = string.Empty;

    public event EventHandler? LayoutRefreshRequested;

    public ObsEditorViewModel(
        AppSettings settings,
        Action<bool>? setObsOverlayEnabled = null,
        Action? restartObsOverlay = null,
        Action<Spectralis.Core.Integrations.Obs.ObsLayout>? setObsLayout = null,
        Action<string, Spectralis.Core.Integrations.Obs.ObsLayout>? setNamedLayout = null,
        Action<string>? removeNamedLayout = null)
    {
        _settings = settings;
        _setObsOverlayEnabled = setObsOverlayEnabled;
        _restartObsOverlay = restartObsOverlay;
        _setObsLayout = setObsLayout;
        _setNamedLayout = setNamedLayout;
        _removeNamedLayout = removeNamedLayout;
        _customObsJson = settings.ObsLayoutJson;
        RefreshOverlayOptions();
        RefreshUserPresets();
    }

    // ─── Overlay management ───────────────────────────────────────────────────
    public ObservableCollection<ObsOverlayOption> OverlayOptions { get; } = [];

    public ObsOverlayOption? SelectedOverlay
    {
        get => _selectedOverlay;
        set
        {
            if (_selectedOverlay == value) return;
            this.RaiseAndSetIfChanged(ref _selectedOverlay, value);
            this.RaisePropertyChanged(nameof(SelectedOverlayUrl));
            this.RaisePropertyChanged(nameof(CanRemoveSelectedOverlay));
            _customObsJson = GetLayoutJson(value?.Id);
            this.RaisePropertyChanged(nameof(CustomObsJson));
            LayoutRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedOverlayUrl
    {
        get
        {
            var token = _settings.ObsOverlayToken;
            if (string.IsNullOrWhiteSpace(token)) return _obsOverlayUrl;
            var baseUrl = $"http://127.0.0.1:{_settings.ObsOverlayPort}/obs/{token}";
            var id = _selectedOverlay?.Id;
            return string.IsNullOrWhiteSpace(id)
                ? baseUrl
                : $"{baseUrl}/o/{Uri.EscapeDataString(id)}";
        }
    }

    public bool CanRemoveSelectedOverlay => _selectedOverlay is { IsDefault: false };

    public bool IsAddingOverlay
    {
        get => _isAddingOverlay;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isAddingOverlay, value);
            this.RaisePropertyChanged(nameof(CanConfirmAddOverlay));
        }
    }

    public string NewOverlayName
    {
        get => _newOverlayName;
        set
        {
            this.RaiseAndSetIfChanged(ref _newOverlayName, value);
            this.RaisePropertyChanged(nameof(CanConfirmAddOverlay));
        }
    }

    public bool CanConfirmAddOverlay => _isAddingOverlay && !string.IsNullOrWhiteSpace(_newOverlayName);

    public void BeginAddOverlay()
    {
        NewOverlayName = string.Empty;
        IsAddingOverlay = true;
    }

    public void ConfirmAddOverlay()
    {
        if (!CanConfirmAddOverlay) return;
        var name = _newOverlayName.Trim();
        var id = GenerateOverlayId(name);
        var defaultLayout = Spectralis.Core.Integrations.Obs.ObsLayout.CreateDefault();
        _settings.ObsNamedLayouts[id] = defaultLayout.ToJson();
        AppSettingsStore.Save(_settings);
        _setNamedLayout?.Invoke(id, defaultLayout);
        IsAddingOverlay = false;
        RefreshOverlayOptions();
        SelectedOverlay = OverlayOptions.FirstOrDefault(o => o.Id == id) ?? SelectedOverlay;
        ObsStatus = $"Overlay \"{name}\" created.";
    }

    public void CancelAddOverlay()
    {
        IsAddingOverlay = false;
        NewOverlayName = string.Empty;
    }

    public void RemoveSelectedOverlay()
    {
        if (_selectedOverlay is not { IsDefault: false } overlay) return;
        _settings.ObsNamedLayouts.Remove(overlay.Id);
        AppSettingsStore.Save(_settings);
        _removeNamedLayout?.Invoke(overlay.Id);
        RefreshOverlayOptions();
        SelectedOverlay = OverlayOptions.FirstOrDefault();
        ObsStatus = $"Overlay \"{overlay.DisplayName}\" removed.";
    }

    // ─── Layout read / apply ─────────────────────────────────────────────────
    public Spectralis.Core.Integrations.Obs.ObsLayout GetCurrentLayout() =>
        Spectralis.Core.Integrations.Obs.ObsLayout.FromJson(GetLayoutJson(_selectedOverlay?.Id))
            ?? Spectralis.Core.Integrations.Obs.ObsLayout.CreateDefault();

    public void ApplyDesignerLayout(Spectralis.Core.Integrations.Obs.ObsLayout layout)
    {
        ApplyToCurrentOverlay(layout);
        ObsStatus = "Designer layout applied. Reload the browser source.";
    }

    private void ApplyToCurrentOverlay(Spectralis.Core.Integrations.Obs.ObsLayout layout)
    {
        var id = _selectedOverlay?.Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            _settings.ObsLayoutJson = layout.ToJson();
            _setObsLayout?.Invoke(layout);
        }
        else
        {
            _settings.ObsNamedLayouts[id] = layout.ToJson();
            _setNamedLayout?.Invoke(id, layout);
        }
        AppSettingsStore.Save(_settings);
        _customObsJson = layout.ToJson();
        this.RaisePropertyChanged(nameof(CustomObsJson));
    }

    // ─── Preset management ────────────────────────────────────────────────────
    public Spectralis.Core.Integrations.Obs.ObsPreset[] BuiltInPresets { get; } =
        [.. Spectralis.Core.Integrations.Obs.BuiltInObsPresets.All];

    public ObservableCollection<Spectralis.Core.Integrations.Obs.ObsPreset> UserPresets { get; } = [];

    public Spectralis.Core.Integrations.Obs.ObsPreset? SelectedBuiltInPreset
    {
        get => _selectedBuiltInPreset;
        set => this.RaiseAndSetIfChanged(ref _selectedBuiltInPreset, value);
    }

    public Spectralis.Core.Integrations.Obs.ObsPreset? SelectedUserPreset
    {
        get => _selectedUserPreset;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedUserPreset, value);
            this.RaisePropertyChanged(nameof(HasSelectedUserPreset));
        }
    }

    public string NewPresetName
    {
        get => _newPresetName;
        set
        {
            this.RaiseAndSetIfChanged(ref _newPresetName, value);
            this.RaisePropertyChanged(nameof(CanSavePreset));
        }
    }

    public bool CanSavePreset => !string.IsNullOrWhiteSpace(_newPresetName);
    public bool HasSelectedUserPreset => _selectedUserPreset is not null;

    public void ApplyBuiltInPreset()
    {
        if (_selectedBuiltInPreset?.Layout is not { } layout) return;
        ApplyToCurrentOverlay(layout);
        ObsStatus = $"Preset \"{_selectedBuiltInPreset.Name}\" applied.";
    }

    public void ApplyUserPreset()
    {
        if (_selectedUserPreset?.Layout is not { } layout) return;
        ApplyToCurrentOverlay(layout);
        ObsStatus = $"Preset \"{_selectedUserPreset.Name}\" applied.";
    }

    public void SaveCurrentAsPreset()
    {
        if (!CanSavePreset) return;
        var name = _newPresetName.Trim();
        var json = GetLayoutJson(_selectedOverlay?.Id);
        var existing = _settings.ObsUserPresets.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            existing.LayoutJson = json;
        else
            _settings.ObsUserPresets.Add(new Spectralis.Core.Integrations.Obs.ObsPreset { Name = name, LayoutJson = json });
        AppSettingsStore.Save(_settings);
        RefreshUserPresets();
        SelectedUserPreset = UserPresets.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        NewPresetName = string.Empty;
        ObsStatus = $"Preset \"{name}\" saved.";
    }

    public void DeleteSelectedUserPreset()
    {
        if (_selectedUserPreset is not { } preset) return;
        _settings.ObsUserPresets.RemoveAll(p =>
            string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        AppSettingsStore.Save(_settings);
        RefreshUserPresets();
        SelectedUserPreset = null;
        ObsStatus = $"Preset \"{preset.Name}\" deleted.";
    }

    public void ApplyPresetLayout(Spectralis.Core.Integrations.Obs.ObsPreset preset)
    {
        if (preset.Layout is not { } layout) return;
        ApplyToCurrentOverlay(layout);
        LayoutRefreshRequested?.Invoke(this, EventArgs.Empty);
        ObsStatus = $"Preset \"{preset.Name}\" applied.";
    }

    public void DeletePreset(Spectralis.Core.Integrations.Obs.ObsPreset preset)
    {
        _settings.ObsUserPresets.RemoveAll(p =>
            string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        AppSettingsStore.Save(_settings);
        RefreshUserPresets();
        ObsStatus = $"Preset \"{preset.Name}\" deleted.";
    }

    // ─── JSON editor ─────────────────────────────────────────────────────────
    public string CustomObsJson
    {
        get => _customObsJson;
        set => this.RaiseAndSetIfChanged(ref _customObsJson, value);
    }

    public void ApplyCustomObsJson()
    {
        var layout = Spectralis.Core.Integrations.Obs.ObsLayout.FromJson(_customObsJson);
        if (layout is null)
        {
            ObsStatus = "Custom JSON is not valid ObsLayout JSON. Check widget structure.";
            return;
        }
        ApplyToCurrentOverlay(layout);
        ObsStatus = "Custom layout applied. Reload the browser source.";
    }

    // ─── Connection / enable / token ─────────────────────────────────────────
    public bool EnableObsOverlay
    {
        get => _settings.EnableObsOverlay;
        set
        {
            if (_settings.EnableObsOverlay == value) return;
            _settings.EnableObsOverlay = value;
            AppSettingsStore.Save(_settings);
            _setObsOverlayEnabled?.Invoke(value);
            this.RaisePropertyChanged();
        }
    }

    public string ObsPort
    {
        get => _settings.ObsOverlayPort.ToString();
        set
        {
            if (int.TryParse(value, out var port))
            {
                port = Math.Clamp(port, 1024, 65535);
                if (_settings.ObsOverlayPort == port) return;
                _settings.ObsOverlayPort = port;
                AppSettingsStore.Save(_settings);
                _restartObsOverlay?.Invoke();
                this.RaisePropertyChanged(nameof(SelectedOverlayUrl));
                this.RaisePropertyChanged(nameof(ObsOverlayUrl));
            }
            this.RaisePropertyChanged();
        }
    }

    public string ObsToken => _settings.ObsOverlayToken;

    /// <summary>Declared OBS Browser Source resolution; drives the visual designer's preview aspect ratio.</summary>
    public string ObsCanvasWidth
    {
        get => _settings.ObsCanvasWidth.ToString();
        set
        {
            if (int.TryParse(value, out var width))
            {
                width = Math.Clamp(width, 160, 7680);
                if (_settings.ObsCanvasWidth == width) return;
                _settings.ObsCanvasWidth = width;
                AppSettingsStore.Save(_settings);
                this.RaisePropertyChanged(nameof(CanvasAspectRatio));
            }
            this.RaisePropertyChanged();
        }
    }

    public string ObsCanvasHeight
    {
        get => _settings.ObsCanvasHeight.ToString();
        set
        {
            if (int.TryParse(value, out var height))
            {
                height = Math.Clamp(height, 90, 4320);
                if (_settings.ObsCanvasHeight == height) return;
                _settings.ObsCanvasHeight = height;
                AppSettingsStore.Save(_settings);
                this.RaisePropertyChanged(nameof(CanvasAspectRatio));
            }
            this.RaisePropertyChanged();
        }
    }

    /// <summary>Width/height ratio for the visual designer's preview canvas.</summary>
    public double CanvasAspectRatio => (double)_settings.ObsCanvasWidth / _settings.ObsCanvasHeight;

    public void RegenerateObsToken()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _settings.ObsOverlayToken = token;
        AppSettingsStore.Save(_settings);
        _restartObsOverlay?.Invoke();
        this.RaisePropertyChanged(nameof(ObsToken));
        this.RaisePropertyChanged(nameof(ObsOverlayUrl));
        this.RaisePropertyChanged(nameof(SelectedOverlayUrl));
    }

    public string ObsOverlayUrl
    {
        get => _obsOverlayUrl;
        set
        {
            this.RaiseAndSetIfChanged(ref _obsOverlayUrl, value);
            this.RaisePropertyChanged(nameof(SelectedOverlayUrl));
        }
    }

    public string ObsStatus
    {
        get => _obsStatus;
        set => this.RaiseAndSetIfChanged(ref _obsStatus, value);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────
    private void RefreshOverlayOptions()
    {
        var currentId = _selectedOverlay?.Id;
        OverlayOptions.Clear();
        OverlayOptions.Add(new ObsOverlayOption { Id = string.Empty, DisplayName = "Default" });
        foreach (var key in _settings.ObsNamedLayouts.Keys)
            OverlayOptions.Add(new ObsOverlayOption { Id = key, DisplayName = key });
        _selectedOverlay = OverlayOptions.FirstOrDefault(o => o.Id == currentId) ?? OverlayOptions[0];
        this.RaisePropertyChanged(nameof(SelectedOverlay));
        this.RaisePropertyChanged(nameof(SelectedOverlayUrl));
        this.RaisePropertyChanged(nameof(CanRemoveSelectedOverlay));
    }

    private void RefreshUserPresets()
    {
        var currentName = _selectedUserPreset?.Name;
        UserPresets.Clear();
        foreach (var p in _settings.ObsUserPresets) UserPresets.Add(p);
        _selectedUserPreset = UserPresets.FirstOrDefault(p => p.Name == currentName);
        this.RaisePropertyChanged(nameof(SelectedUserPreset));
        this.RaisePropertyChanged(nameof(HasSelectedUserPreset));
    }

    private string GetLayoutJson(string? overlayId)
    {
        if (!string.IsNullOrWhiteSpace(overlayId)
            && _settings.ObsNamedLayouts.TryGetValue(overlayId, out var json)
            && !string.IsNullOrWhiteSpace(json))
            return json;
        return _settings.ObsLayoutJson;
    }

    private string GenerateOverlayId(string name)
    {
        var trimmed = name.Trim();
        if (!_settings.ObsNamedLayouts.ContainsKey(trimmed)) return trimmed;
        var i = 2;
        while (_settings.ObsNamedLayouts.ContainsKey($"{trimmed} ({i})")) i++;
        return $"{trimmed} ({i})";
    }
}

public enum SettingsCategory
{
    Appearance,
    Visualizer,
    Playback,
    Library,
    Integrations,
    SharedPlay,
    Updates,
    About,
    Developer,
}

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly NowPlayingViewModel _nowPlaying;
    private readonly Action<bool>? _setDiscordPresenceEnabled;
    private string _registrationStatus = string.Empty;
    private string _diagnosticsStatus = string.Empty;
    private string _updateStatus = string.Empty;
    private string _cacheStatus = string.Empty;
    private string _spotifyStatus = string.Empty;
    private readonly SpotifyService _spotify = new();
    private SelectionOption<AppThemeMode> _selectedThemeMode;
    private SelectionOption<AppThemeAccent> _selectedThemeAccent;
    private VisualizerOption _selectedDefaultVisualizer;
    private SelectionOption<MidiPlaybackInstrument> _selectedMidiInstrument;
    private SettingsCategory _selectedCategory = SettingsCategory.Appearance;
    private int _versionClickCount;
    private DateTimeOffset _lastVersionClickAtUtc;

    public SettingsViewModel(
        AppSettings settings,
        NowPlayingViewModel nowPlaying,
        Action<bool>? setDiscordPresenceEnabled = null,
        ObsEditorViewModel? obsEditor = null,
        LibraryViewModel? library = null)
    {
        _settings = settings;
        _nowPlaying = nowPlaying;
        _setDiscordPresenceEnabled = setDiscordPresenceEnabled;
        ObsEditor = obsEditor ?? new ObsEditorViewModel(settings);
        Library = library;
        ThemeModeOptions = Enum.GetValues<AppThemeMode>()
            .Select(mode => new SelectionOption<AppThemeMode>(ThemeModeLabel(mode), mode))
            .ToArray();
        ThemeAccentOptions = Enum.GetValues<AppThemeAccent>()
            .Select(accent => new SelectionOption<AppThemeAccent>(ThemeAccentLabel(accent), accent))
            .ToArray();
        _selectedThemeMode = ThemeModeOptions.First(option => option.Value == _settings.ThemeMode);
        _selectedThemeAccent = ThemeAccentOptions.First(option => option.Value == _settings.ThemeAccent);
        _selectedDefaultVisualizer = VisualizerOptions.FirstOrDefault(option => option.Mode == _settings.DefaultVisualizer)
            ?? VisualizerOptions.First(option => option.Mode == VisualizerMode.MirrorSpectrum);
        MidiInstrumentOptions = MidiPlaybackInstrumentCatalog.GetOptions()
            .Select(option => new SelectionOption<MidiPlaybackInstrument>(option.Label, option.Value))
            .ToArray();
        _selectedMidiInstrument = MidiInstrumentOptions.FirstOrDefault(option => option.Value == _settings.MidiInstrument)
            ?? MidiInstrumentOptions.First();
        RefreshCategoryOptions();
        _nowPlaying.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(NowPlayingViewModel.PeakHold):
                case nameof(NowPlayingViewModel.VisualizerSensitivityPercent):
                case nameof(NowPlayingViewModel.AutoCycleVisualizers):
                case nameof(NowPlayingViewModel.SelectedCycleDuration):
                case nameof(NowPlayingViewModel.SelectedSampleRate):
                case nameof(NowPlayingViewModel.VolumePercent):
                    this.RaisePropertyChanged(e.PropertyName);
                    break;
            }
        };

        RegisterDefaultAppCommand = ReactiveCommand.Create(RegisterDefaultApp);
        CheckForUpdatesCommand = ReactiveCommand.Create(CheckForUpdates);
        OpenLogsFolderCommand = ReactiveCommand.Create(OpenLogsFolder);
        LinkSpotifyCommand   = ReactiveCommand.CreateFromTask(LinkSpotifyAsync);
        UnlinkSpotifyCommand = ReactiveCommand.Create(UnlinkSpotify);
        RefreshSpotifyStatus();
        RefreshRegistrationStatus();
        var pendingUpdateVersion = AppUpdateNoticeStore.ConsumePending();
        if (!string.IsNullOrWhiteSpace(pendingUpdateVersion))
        {
            _settings.LastSeenAppVersion = DiagnosticsSnapshot.CurrentVersion;
            AppSettingsStore.Save(_settings);
            SpectralisLog.Info($"Consumed pending update notice for {pendingUpdateVersion}.");
            ConsumedUpdateVersion = pendingUpdateVersion;
            RefreshUpdateStatus($"Spectralis updated to {pendingUpdateVersion}.");
        }
        else
        {
            RefreshUpdateStatus();
        }

        DiagnosticsStatus = $"Diagnostics folder: {AppLogPaths.LogDirectory}";
        RefreshCacheStatus();
    }

    public ReactiveCommand<Unit, Unit> RegisterDefaultAppCommand { get; }
    public ReactiveCommand<Unit, Unit> CheckForUpdatesCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenLogsFolderCommand { get; }
    public ReactiveCommand<Unit, Unit> LinkSpotifyCommand { get; }
    public ReactiveCommand<Unit, Unit> UnlinkSpotifyCommand { get; }

    public string SpotifyCustomClientId
    {
        get => _settings.SpotifyCustomClientId;
        set
        {
            if (_settings.SpotifyCustomClientId == value) return;
            _settings.SpotifyCustomClientId = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    /// <summary>Hide the custom client ID field entirely when a build-baked default already covers it.</summary>
    public bool ShowSpotifyCustomClientIdField => !SpotifyClientIdProvider.HasDefaultClientId;

    public string SpotifyStatus
    {
        get => _spotifyStatus;
        private set => this.RaiseAndSetIfChanged(ref _spotifyStatus, value);
    }

    public bool SpotifyIsLinked => _spotify.IsLinked;

    public bool SharedPlayEnabled
    {
        get => _settings.SharedPlayEnabled;
        set
        {
            if (_settings.SharedPlayEnabled == value) return;
            _settings.SharedPlayEnabled = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public string SharedPlayCdnBaseUrl
    {
        get => _settings.SharedPlayCdnBaseUrl;
        set
        {
            if (_settings.SharedPlayCdnBaseUrl == value) return;
            _settings.SharedPlayCdnBaseUrl = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public bool SharedPlayLiveChannelEnabled
    {
        get => _settings.SharedPlayLiveChannelEnabled;
        set
        {
            if (_settings.SharedPlayLiveChannelEnabled == value) return;
            _settings.SharedPlayLiveChannelEnabled = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public string SharedPlayLiveChannelId
    {
        get => _settings.SharedPlayLiveChannelId;
        set
        {
            if (_settings.SharedPlayLiveChannelId == value) return;
            _settings.SharedPlayLiveChannelId = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public string SharedPlayLiveChannelOwnerToken
    {
        get => _settings.SharedPlayLiveChannelOwnerToken;
        set
        {
            if (_settings.SharedPlayLiveChannelOwnerToken == value) return;
            _settings.SharedPlayLiveChannelOwnerToken = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public string SharedPlayLiveChannelDisplayName
    {
        get => _settings.SharedPlayLiveChannelDisplayName;
        set
        {
            if (_settings.SharedPlayLiveChannelDisplayName == value) return;
            _settings.SharedPlayLiveChannelDisplayName = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public ObsEditorViewModel ObsEditor { get; }

    public IReadOnlyList<SelectionOption<AppThemeMode>> ThemeModeOptions { get; }
    public IReadOnlyList<SelectionOption<AppThemeAccent>> ThemeAccentOptions { get; }
    public IReadOnlyList<VisualizerOption> VisualizerOptions => _nowPlaying.VisualizerOptions;
    public IReadOnlyList<SelectionOption<int>> SampleRateOptions => _nowPlaying.SampleRateOptions;
    public IReadOnlyList<SelectionOption<int>> CycleDurationOptions => _nowPlaying.CycleDurationOptions;
    public IReadOnlyList<SelectionOption<MidiPlaybackInstrument>> MidiInstrumentOptions { get; }

    public SelectionOption<AppThemeMode> SelectedThemeMode
    {
        get => _selectedThemeMode;
        set
        {
            if (value is null || _selectedThemeMode == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedThemeMode, value);
            _settings.ThemeMode = value.Value;
            AppSettingsStore.Save(_settings);
            AppThemeService.Apply(_settings);
        }
    }

    public SelectionOption<AppThemeAccent> SelectedThemeAccent
    {
        get => _selectedThemeAccent;
        set
        {
            if (value is null || _selectedThemeAccent == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedThemeAccent, value);
            _settings.ThemeAccent = value.Value;
            AppSettingsStore.Save(_settings);
            AppThemeService.Apply(_settings);
        }
    }

    public bool UseEmbeddedTrackThemes
    {
        get => _settings.UseEmbeddedTrackThemes;
        set
        {
            if (_settings.UseEmbeddedTrackThemes == value)
            {
                return;
            }

            _settings.UseEmbeddedTrackThemes = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public bool EnableEmbeddedContent
    {
        get => _settings.EnableEmbeddedContent;
        set
        {
            if (_settings.EnableEmbeddedContent == value)
            {
                return;
            }

            _settings.EnableEmbeddedContent = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public bool ShowMoreInfo
    {
        get => _nowPlaying.ShowMoreInfo;
        set
        {
            _nowPlaying.ShowMoreInfo = value;
            this.RaisePropertyChanged();
        }
    }

    public VisualizerOption SelectedDefaultVisualizer
    {
        get => _selectedDefaultVisualizer;
        set
        {
            if (value is null)
            {
                return;
            }

            if (_selectedDefaultVisualizer == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedDefaultVisualizer, value);
            _nowPlaying.ApplyDefaultVisualizer(value.Mode);
        }
    }

    public bool PeakHold
    {
        get => _nowPlaying.PeakHold;
        set
        {
            _nowPlaying.PeakHold = value;
            this.RaisePropertyChanged();
        }
    }

    public int VisualizerSensitivityPercent
    {
        get => _nowPlaying.VisualizerSensitivityPercent;
        set
        {
            _nowPlaying.VisualizerSensitivityPercent = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(VisualizerSensitivityText));
        }
    }

    public string VisualizerSensitivityText => _nowPlaying.VisualizerSensitivityText;

    public bool AutoCycleVisualizers
    {
        get => _nowPlaying.AutoCycleVisualizers;
        set
        {
            _nowPlaying.AutoCycleVisualizers = value;
            this.RaisePropertyChanged();
        }
    }

    public SelectionOption<int> SelectedCycleDuration
    {
        get => _nowPlaying.SelectedCycleDuration;
        set
        {
            _nowPlaying.SelectedCycleDuration = value;
            this.RaisePropertyChanged();
        }
    }

    public SelectionOption<int> SelectedSampleRate
    {
        get => _nowPlaying.SelectedSampleRate;
        set
        {
            _nowPlaying.SelectedSampleRate = value;
            this.RaisePropertyChanged();
        }
    }

    public SelectionOption<MidiPlaybackInstrument> SelectedMidiInstrument
    {
        get => _selectedMidiInstrument;
        set
        {
            if (value is null)
            {
                return;
            }

            if (_selectedMidiInstrument == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _selectedMidiInstrument, value);
            _nowPlaying.ApplyMidiInstrument(value.Value);
        }
    }

    public double DefaultVolume
    {
        get => _nowPlaying.VolumePercent;
        set
        {
            _nowPlaying.VolumePercent = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(DefaultVolumeText));
        }
    }

    public string DefaultVolumeText => $"{DefaultVolume:0}%";

    public bool AutoPlayOnOpen
    {
        get => _settings.AutoPlayOnOpen;
        set
        {
            if (_settings.AutoPlayOnOpen == value)
            {
                return;
            }

            _settings.AutoPlayOnOpen = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public bool QueueByDefault
    {
        get => _settings.QueueByDefault;
        set
        {
            if (_settings.QueueByDefault == value)
            {
                return;
            }

            _settings.QueueByDefault = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    /// <summary>Library view model, when hosted in the full shell; drives the watched-folders section.</summary>
    public LibraryViewModel? Library { get; }

    public bool LibraryAutoScanOnOpen
    {
        get => _settings.LibraryAutoScanOnOpen;
        set
        {
            if (_settings.LibraryAutoScanOnOpen == value)
            {
                return;
            }

            _settings.LibraryAutoScanOnOpen = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public bool PreserveSession
    {
        get => _settings.PreserveSession;
        set
        {
            if (_settings.PreserveSession == value)
            {
                return;
            }

            _settings.PreserveSession = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public bool AutoAnalyzeBpm
    {
        get => _settings.AutoAnalyzeBpm;
        set
        {
            if (_settings.AutoAnalyzeBpm == value)
            {
                return;
            }

            _settings.AutoAnalyzeBpm = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public bool RememberWindowPlacement
    {
        get => _settings.RememberWindowPlacement;
        set
        {
            if (_settings.RememberWindowPlacement == value)
            {
                return;
            }

            _settings.RememberWindowPlacement = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set
        {
            if (_settings.CloseToTray == value)
            {
                return;
            }

            _settings.CloseToTray = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public bool EnableDiscordRichPresence
    {
        get => _settings.EnableDiscordRichPresence;
        set
        {
            if (_settings.EnableDiscordRichPresence == value)
            {
                return;
            }

            _settings.EnableDiscordRichPresence = value;
            AppSettingsStore.Save(_settings);
            _setDiscordPresenceEnabled?.Invoke(value);
            this.RaisePropertyChanged();
        }
    }

    public bool EnableClipboardUrlMonitoring
    {
        get => _settings.EnableClipboardUrlMonitoring;
        set
        {
            if (_settings.EnableClipboardUrlMonitoring == value)
            {
                return;
            }

            _settings.EnableClipboardUrlMonitoring = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
        }
    }

    public bool EnableAutoUpdates
    {
        get => _settings.EnableAutoUpdates;
        set
        {
            if (_settings.EnableAutoUpdates == value)
            {
                return;
            }

            _settings.EnableAutoUpdates = value;
            AppSettingsStore.Save(_settings);
            this.RaisePropertyChanged();
            RefreshUpdateStatus();
        }
    }

    public string VersionText => $"Spectralis {DiagnosticsSnapshot.CurrentVersion}";

    /// <summary>Non-null when a Squirrel/Velopack update notice was consumed on startup this session.</summary>
    public string? ConsumedUpdateVersion { get; private set; }

    public bool DeveloperModeUnlocked => _settings.DeveloperModeUnlocked;

    /// <summary>Click the version number 5 times within 3 seconds to reveal the Developer category.</summary>
    public void RegisterVersionClick()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastVersionClickAtUtc > TimeSpan.FromSeconds(3))
        {
            _versionClickCount = 0;
        }
        _lastVersionClickAtUtc = now;
        _versionClickCount++;

        if (_settings.DeveloperModeUnlocked || _versionClickCount < 5)
        {
            return;
        }

        _versionClickCount = 0;
        _settings.DeveloperModeUnlocked = true;
        AppSettingsStore.Save(_settings);
        this.RaisePropertyChanged(nameof(DeveloperModeUnlocked));
        RefreshCategoryOptions();
        DiagnosticsStatus = "Developer tools unlocked.";
    }

    public IReadOnlyList<SelectionOption<SettingsCategory>> CategoryOptions { get; private set; } = [];

    public SelectionOption<SettingsCategory> SelectedCategoryOption
    {
        get => CategoryOptions.FirstOrDefault(option => option.Value == _selectedCategory) ?? CategoryOptions[0];
        set
        {
            if (value is null || _selectedCategory == value.Value) return;
            _selectedCategory = value.Value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(IsAppearanceVisible));
            this.RaisePropertyChanged(nameof(IsVisualizerVisible));
            this.RaisePropertyChanged(nameof(IsPlaybackVisible));
            this.RaisePropertyChanged(nameof(IsLibraryVisible));
            this.RaisePropertyChanged(nameof(IsIntegrationsVisible));
            this.RaisePropertyChanged(nameof(IsSharedPlayVisible));
            this.RaisePropertyChanged(nameof(IsUpdatesVisible));
            this.RaisePropertyChanged(nameof(IsAboutVisible));
            this.RaisePropertyChanged(nameof(IsDeveloperVisible));
        }
    }

    public bool IsAppearanceVisible => _selectedCategory == SettingsCategory.Appearance;
    public bool IsVisualizerVisible => _selectedCategory == SettingsCategory.Visualizer;
    public bool IsPlaybackVisible => _selectedCategory == SettingsCategory.Playback;
    public bool IsLibraryVisible => _selectedCategory == SettingsCategory.Library;
    public bool IsIntegrationsVisible => _selectedCategory == SettingsCategory.Integrations;
    public bool IsSharedPlayVisible => _selectedCategory == SettingsCategory.SharedPlay;
    public bool IsUpdatesVisible => _selectedCategory == SettingsCategory.Updates;
    public bool IsAboutVisible => _selectedCategory == SettingsCategory.About;
    public bool IsDeveloperVisible => _selectedCategory == SettingsCategory.Developer;

    private static readonly SettingsCategory[] AllCategories = Enum.GetValues<SettingsCategory>();

    private void RefreshCategoryOptions()
    {
        CategoryOptions = AllCategories
            .Where(category => category != SettingsCategory.Developer || _settings.DeveloperModeUnlocked)
            .Select(category => new SelectionOption<SettingsCategory>(CategoryLabel(category), category))
            .ToArray();
        this.RaisePropertyChanged(nameof(CategoryOptions));
        this.RaisePropertyChanged(nameof(SelectedCategoryOption));
    }

    private static string CategoryLabel(SettingsCategory category) => category switch
    {
        SettingsCategory.SharedPlay => "Shared Play",
        _ => category.ToString(),
    };

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => this.RaiseAndSetIfChanged(ref _updateStatus, value);
    }

    public string DiagnosticsStatus
    {
        get => _diagnosticsStatus;
        private set => this.RaiseAndSetIfChanged(ref _diagnosticsStatus, value);
    }

    public string RegistrationStatus
    {
        get => _registrationStatus;
        private set => this.RaiseAndSetIfChanged(ref _registrationStatus, value);
    }

    private async Task LinkSpotifyAsync()
    {
        SpotifyStatus = "Opening Spotify authorization…";
        try
        {
            var clientId = SpotifyClientIdProvider.ResolveClientId(_settings.SpotifyCustomClientId);
            if (string.IsNullOrWhiteSpace(clientId))
            {
                SpotifyStatus = "No Spotify client ID configured. Enter your client ID above.";
                return;
            }
            var linked = await _spotify.LinkAccountAsync(clientId);
            RefreshSpotifyStatus(linked ? null : "Authorization was cancelled or timed out.");
        }
        catch (Exception ex)
        {
            SpotifyStatus = $"Link failed: {ex.Message}";
        }
    }

    private void UnlinkSpotify()
    {
        _spotify.UnlinkAccount();
        RefreshSpotifyStatus();
    }

    private void RefreshSpotifyStatus(string? overrideMessage = null)
    {
        this.RaisePropertyChanged(nameof(SpotifyIsLinked));
        if (!string.IsNullOrEmpty(overrideMessage))
        {
            SpotifyStatus = overrideMessage;
            return;
        }
        SpotifyStatus = _spotify.IsLinked
            ? $"Linked as {_spotify.AccountDisplayName ?? _spotify.AccountEmail ?? "unknown account"}"
            : "Not linked.";
    }

    private void RegisterDefaultApp()
    {
        if (!OperatingSystem.IsWindows())
        {
            RegistrationStatus = "Default-app registration is Windows-only for now.";
            return;
        }

        try
        {
            var registrar = new WindowsProtocolRegistrar();
            registrar.RegisterProtocol();
            registrar.RegisterFileAssociations(
                SupportedAudioFormats.Extensions.Concat([".spectralis", ".spectral"]).ToArray());
            RefreshRegistrationStatus();
        }
        catch (Exception ex)
        {
            RegistrationStatus = $"Registration failed: {ex.Message}";
        }
    }

    private void RefreshRegistrationStatus()
    {
        if (!OperatingSystem.IsWindows())
        {
            RegistrationStatus = "Default-app registration is Windows-only for now.";
            return;
        }

        var registrar = new WindowsProtocolRegistrar();
        var protocol = registrar.IsProtocolRegistered();
        var files = registrar.AreFileAssociationsRegistered();
        RegistrationStatus = (protocol, files) switch
        {
            (true, true) => "spectralis:// protocol and file associations registered",
            (true, false) => "Protocol registered; file associations not registered",
            (false, true) => "File associations registered; protocol not registered",
            _ => "Not registered",
        };
    }

    private static string ThemeModeLabel(AppThemeMode mode) =>
        mode switch
        {
            AppThemeMode.Oled => "OLED",
            _ => mode.ToString(),
        };

    private static string ThemeAccentLabel(AppThemeAccent accent) => accent.ToString();

    public string BuildDiagnosticsText() => DiagnosticsSnapshot.Build();

    public void MarkDiagnosticsCopied()
    {
        DiagnosticsStatus = "Copied diagnostics snapshot.";
        SpectralisLog.Info("Diagnostics snapshot copied to clipboard.");
    }

    public void MarkDiagnosticsCopyFailed(string message)
    {
        DiagnosticsStatus = $"Could not copy diagnostics: {message}";
        SpectralisLog.Warn(DiagnosticsStatus);
    }

    public void CheckForUpdates()
    {
        _settings.LastSeenAppVersion = DiagnosticsSnapshot.CurrentVersion;
        AppSettingsStore.Save(_settings);
        SpectralisLog.Info("Manual update check requested.");
        RefreshUpdateStatus("Checking for updates...");
    }

    public void ApplyUpdateFeedResult(Spectralis.Core.Platform.ReleaseFeedResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            RefreshUpdateStatus($"Update check failed: {result.ErrorMessage}");
            SpectralisLog.Warn($"Update feed check failed: {result.ErrorMessage}");
            return;
        }

        if (!result.IsUpdateAvailable)
        {
            RefreshUpdateStatus("Spectralis is up to date.");
            SpectralisLog.Info("Update check: up to date.");
            return;
        }

        SpectralisLog.Info($"Update check: {result.LatestVersion} is available.");
        RefreshUpdateStatus($"Spectralis {result.LatestVersion} is available.");
    }

    public void SaveIgnoredUpdateVersion(string version)
    {
        _settings.IgnoredUpdateVersion = version;
        AppSettingsStore.Save(_settings);
    }

    public string? IgnoredUpdateVersion => _settings.IgnoredUpdateVersion;

    private void RefreshUpdateStatus(string? overrideStatus = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideStatus))
        {
            UpdateStatus = overrideStatus;
            return;
        }

        UpdateStatus = _settings.EnableAutoUpdates
            ? "Automatic updates are on."
            : "Automatic updates are off.";
    }

    private void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(AppLogPaths.LogDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppLogPaths.LogDirectory,
                UseShellExecute = true,
            });
            DiagnosticsStatus = "Opened diagnostics folder.";
        }
        catch (Exception ex)
        {
            DiagnosticsStatus = $"Could not open diagnostics folder: {ex.Message}";
            SpectralisLog.Error("Failed to open diagnostics folder.", ex);
        }
    }

    public string CacheStatus
    {
        get => _cacheStatus;
        private set => this.RaiseAndSetIfChanged(ref _cacheStatus, value);
    }

    public void RefreshCacheStatus()
    {
        var bytes = RemoteAudioCache.GetCacheSizeBytes();
        CacheStatus = $"Remote audio cache: {(bytes > 0 ? FormatBytes(bytes) : "empty")}";
    }

    public void SetCacheClearedStatus(long freed, long remaining)
    {
        CacheStatus = freed > 0
            ? $"Cleared {FormatBytes(freed)}.{(remaining > 0 ? $" {FormatBytes(remaining)} still in use." : string.Empty)}"
            : "Remote audio cache was empty.";
    }

    private static string FormatBytes(long bytes) =>
        bytes >= 1_048_576
            ? $"{bytes / 1_048_576.0:0.#} MB"
            : bytes >= 1024
                ? $"{bytes / 1024.0:0.#} KB"
                : $"{bytes} B";

}
