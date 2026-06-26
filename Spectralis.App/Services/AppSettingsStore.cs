using System.Text.Json;
using System.Text.Json.Serialization;
using Spectralis.Core.Audio.Midi;
using Spectralis.Core.Integrations.Obs;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Services;

public sealed class AppSettings
{
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.Dark;
    public AppThemeAccent ThemeAccent { get; set; } = AppThemeAccent.Amber;
    public bool UseEmbeddedTrackThemes { get; set; } = true;
    public bool EnableEmbeddedContent { get; set; } = true;
    public bool ShowMoreInfo { get; set; } = true;
    public VisualizerMode CurrentVisualizer { get; set; } = VisualizerMode.MirrorSpectrum;
    public VisualizerMode DefaultVisualizer { get; set; } = VisualizerMode.MirrorSpectrum;
    public bool ShowVisualizer { get; set; } = true;
    public bool PeakHold { get; set; } = true;
    public int VisualizerSensitivity { get; set; } = 100;
    public bool EnableVisualizerAutoCycle { get; set; } = false;
    public int VisualizerCycleSeconds { get; set; } = 12;
    public int PreferredSampleRate { get; set; }
    public MidiPlaybackInstrument MidiInstrument { get; set; } = MidiPlaybackInstrument.AcousticGrandPiano;
    public int DefaultVolume { get; set; } = 85;
    public bool AutoPlayOnOpen { get; set; } = true;
    public bool QueueByDefault { get; set; }
    public bool RememberWindowPlacement { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool PreserveSession { get; set; } = true;
    public bool ExternalApiConsentAccepted { get; set; }
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public bool EnableDiscordRichPresence { get; set; } = true;
    public bool EnableObsOverlay { get; set; } = true;
    public int ObsOverlayPort { get; set; } = 5128;
    public string ObsOverlayToken { get; set; } = string.Empty;
    /// <summary>Declared OBS Browser Source resolution; drives the visual designer's preview aspect ratio.</summary>
    public int ObsCanvasWidth { get; set; } = 1920;
    public int ObsCanvasHeight { get; set; } = 1080;
    /// <summary>Serialized ObsLayout JSON for the default overlay, or empty to use the built-in default.</summary>
    public string ObsLayoutJson { get; set; } = string.Empty;
    /// <summary>Named overlay layouts: overlayId → layoutJson. Each gets its own URL at /obs/{token}/o/{id}</summary>
    public Dictionary<string, string> ObsNamedLayouts { get; set; } = [];
    /// <summary>User-saved layout presets.</summary>
    public List<ObsPreset> ObsUserPresets { get; set; } = [];
    public bool EnableClipboardUrlMonitoring { get; set; } = true;
    public bool EnableAutoUpdates { get; set; } = true;
    public List<string> LibraryFolders { get; set; } = [];
    public bool LibraryAutoScanOnOpen { get; set; } = true;
    public bool AutoAnalyzeBpm { get; set; } = true;
    public bool LastFmEnabled { get; set; }
    public string LastFmApiKey { get; set; } = string.Empty;
    public string LastFmApiSecret { get; set; } = string.Empty;
    public string LastFmSessionKey { get; set; } = string.Empty;
    public string LastFmUsername { get; set; } = string.Empty;
    public bool ListenBrainzEnabled { get; set; }
    public string ListenBrainzToken { get; set; } = string.Empty;
    public string ListenBrainzUsername { get; set; } = string.Empty;
    public string LastSeenAppVersion { get; set; } = string.Empty;
    public string IgnoredUpdateVersion { get; set; } = string.Empty;
    public string SpotifyCustomClientId { get; set; } = string.Empty;
    public bool SharedPlayEnabled { get; set; }
    public string SharedPlayCdnBaseUrl { get; set; } = string.Empty;
    public bool SharedPlayLiveChannelEnabled { get; set; }
    public string SharedPlayLiveChannelId { get; set; } = string.Empty;
    public string SharedPlayLiveChannelOwnerToken { get; set; } = string.Empty;
    public string SharedPlayLiveChannelDisplayName { get; set; } = string.Empty;
    public bool SidebarCollapsed { get; set; } = true;
    /// <summary>Unlocked by clicking the version number 5 times in Settings; reveals the Developer Tools section.</summary>
    public bool DeveloperModeUnlocked { get; set; }

    /// <summary>Set after the first-launch UI Reveal sequence has played (or been skipped) once.</summary>
    public bool HasSeenUiReveal { get; set; }

    /// <summary>IDs of CDN warning.json notices the user has already dismissed.</summary>
    public List<string> DismissedWarningIds { get; set; } = [];

    public AppSettings Clone() =>
        new()
        {
            ThemeMode = ThemeMode,
            ThemeAccent = ThemeAccent,
            UseEmbeddedTrackThemes = UseEmbeddedTrackThemes,
            EnableEmbeddedContent = EnableEmbeddedContent,
            ShowMoreInfo = ShowMoreInfo,
            CurrentVisualizer = CurrentVisualizer,
            DefaultVisualizer = DefaultVisualizer,
            ShowVisualizer = ShowVisualizer,
            PeakHold = PeakHold,
            VisualizerSensitivity = VisualizerSensitivity,
            EnableVisualizerAutoCycle = EnableVisualizerAutoCycle,
            VisualizerCycleSeconds = VisualizerCycleSeconds,
            PreferredSampleRate = PreferredSampleRate,
            MidiInstrument = MidiInstrument,
            DefaultVolume = DefaultVolume,
            AutoPlayOnOpen = AutoPlayOnOpen,
            QueueByDefault = QueueByDefault,
            RememberWindowPlacement = RememberWindowPlacement,
            MinimizeToTray = MinimizeToTray,
            PreserveSession = PreserveSession,
            ExternalApiConsentAccepted = ExternalApiConsentAccepted,
            WindowX = WindowX,
            WindowY = WindowY,
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight,
            WindowMaximized = WindowMaximized,
            EnableDiscordRichPresence = EnableDiscordRichPresence,
            EnableObsOverlay = EnableObsOverlay,
            ObsOverlayPort = ObsOverlayPort,
            ObsOverlayToken = ObsOverlayToken,
            ObsCanvasWidth = ObsCanvasWidth,
            ObsCanvasHeight = ObsCanvasHeight,
            ObsLayoutJson = ObsLayoutJson,
            ObsNamedLayouts = new Dictionary<string, string>(ObsNamedLayouts),
            ObsUserPresets = ObsUserPresets.Select(p => new ObsPreset { Name = p.Name, LayoutJson = p.LayoutJson }).ToList(),
            EnableClipboardUrlMonitoring = EnableClipboardUrlMonitoring,
            EnableAutoUpdates = EnableAutoUpdates,
            LibraryFolders = LibraryFolders.ToList(),
            LibraryAutoScanOnOpen = LibraryAutoScanOnOpen,
            AutoAnalyzeBpm = AutoAnalyzeBpm,
            LastFmEnabled = LastFmEnabled,
            LastFmApiKey = LastFmApiKey,
            LastFmApiSecret = LastFmApiSecret,
            LastFmSessionKey = LastFmSessionKey,
            LastFmUsername = LastFmUsername,
            ListenBrainzEnabled = ListenBrainzEnabled,
            ListenBrainzToken = ListenBrainzToken,
            ListenBrainzUsername = ListenBrainzUsername,
            LastSeenAppVersion = LastSeenAppVersion,
            IgnoredUpdateVersion = IgnoredUpdateVersion,
            SpotifyCustomClientId = SpotifyCustomClientId,
            SharedPlayEnabled = SharedPlayEnabled,
            SharedPlayCdnBaseUrl = SharedPlayCdnBaseUrl,
            SharedPlayLiveChannelEnabled = SharedPlayLiveChannelEnabled,
            SharedPlayLiveChannelId = SharedPlayLiveChannelId,
            SharedPlayLiveChannelOwnerToken = SharedPlayLiveChannelOwnerToken,
            SharedPlayLiveChannelDisplayName = SharedPlayLiveChannelDisplayName,
            SidebarCollapsed = SidebarCollapsed,
            DeveloperModeUnlocked = DeveloperModeUnlocked,
            HasSeenUiReveal = HasSeenUiReveal,
            DismissedWarningIds = DismissedWarningIds.ToList(),
        };
}

public sealed record SelectionOption<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly int[] AllowedSampleRates = [0, 44100, 48000, 88200, 96000];

    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spectralis",
            "settings-avalonia.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return Normalize(new AppSettings());
            }

            var json = File.ReadAllText(SettingsPath);
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings());
        }
        catch
        {
            return Normalize(new AppSettings());
        }
    }

    public static void Save(AppSettings settings)
    {
        var normalized = Normalize(settings.Clone());
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(normalized, SerializerOptions));
    }

    public static AppSettings Normalize(AppSettings settings)
    {
        settings.ThemeMode = Enum.IsDefined(settings.ThemeMode) ? settings.ThemeMode : AppThemeMode.Dark;
        settings.ThemeAccent = Enum.IsDefined(settings.ThemeAccent) ? settings.ThemeAccent : AppThemeAccent.Amber;
        settings.CurrentVisualizer = NormalizeVisualizer(settings.CurrentVisualizer);
        settings.DefaultVisualizer = NormalizeVisualizer(settings.DefaultVisualizer);
        settings.VisualizerSensitivity = Math.Clamp(settings.VisualizerSensitivity, 50, 200);
        settings.VisualizerCycleSeconds = Math.Clamp(settings.VisualizerCycleSeconds, 5, 60);
        settings.PreferredSampleRate = AllowedSampleRates.Contains(settings.PreferredSampleRate)
            ? settings.PreferredSampleRate
            : 0;
        settings.MidiInstrument = MidiPlaybackInstrumentCatalog.Normalize(settings.MidiInstrument);
        settings.DefaultVolume = Math.Clamp(settings.DefaultVolume, 0, 100);
        settings.ObsOverlayPort = settings.ObsOverlayPort <= 0
            ? 5128
            : Math.Clamp(settings.ObsOverlayPort, 1024, 65535);
        settings.ObsOverlayToken = NormalizeObsToken(settings.ObsOverlayToken);
        settings.ObsCanvasWidth = settings.ObsCanvasWidth <= 0 ? 1920 : Math.Clamp(settings.ObsCanvasWidth, 160, 7680);
        settings.ObsCanvasHeight = settings.ObsCanvasHeight <= 0 ? 1080 : Math.Clamp(settings.ObsCanvasHeight, 90, 4320);
        settings.ObsNamedLayouts ??= [];
        settings.ObsUserPresets = (settings.ObsUserPresets ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .ToList();
        if (settings.WindowWidth < 0 || settings.WindowHeight < 0)
        {
            settings.WindowWidth = 0;
            settings.WindowHeight = 0;
        }
        settings.LibraryFolders = (settings.LibraryFolders ?? [])
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Select(folder => folder.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.LastSeenAppVersion = settings.LastSeenAppVersion?.Trim() ?? string.Empty;
        settings.IgnoredUpdateVersion = settings.IgnoredUpdateVersion?.Trim() ?? string.Empty;
        return settings;
    }

    public static IReadOnlyList<SelectionOption<int>> GetSampleRateOptions() =>
    [
        new("Match source", 0),
        new("44.1 kHz", 44100),
        new("48 kHz", 48000),
        new("88.2 kHz", 88200),
        new("96 kHz", 96000),
    ];

    public static IReadOnlyList<SelectionOption<int>> GetCycleDurationOptions() =>
    [
        new("5 seconds", 5),
        new("8 seconds", 8),
        new("12 seconds", 12),
        new("20 seconds", 20),
        new("30 seconds", 30),
        new("45 seconds", 45),
        new("60 seconds", 60),
    ];

    private static VisualizerMode NormalizeVisualizer(VisualizerMode mode) =>
        VisualizerCatalog.All.Any(definition => definition.Mode == mode)
            ? mode
            : VisualizerMode.MirrorSpectrum;

    private static string NormalizeObsToken(string? token)
    {
        var clean = new string((token ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .ToArray());
        return clean.Length >= 16 ? clean.ToLowerInvariant() : string.Empty;
    }
}
