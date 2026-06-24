using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis;

internal enum ThemeMode
{
    Dark,
    Light,
    Oled,
    Midnight
}

internal enum ThemeAccent
{
    Amber,
    Ocean,
    Rose,
    Forest,
    Violet,
    Crimson,
    Cyan,
    Mint,
    Sunset,
    Gold
}

internal sealed class AppSettings
{
    public ThemeMode ThemeMode { get; set; } = ThemeMode.Dark;
    public ThemeAccent ThemeAccent { get; set; } = ThemeAccent.Amber;
    public bool UseEmbeddedTrackThemes { get; set; } = true;
    public VisualizerMode DefaultVisualizer { get; set; } = VisualizerMode.MirrorSpectrum;
    public string DefaultVisualizerKey { get; set; } = VisualizerChoice.BuiltIn(VisualizerMode.MirrorSpectrum).Key;
    public bool UseEmbeddedTrackVisualizers { get; set; } = true;
    public bool UseEmbeddedTrackContent { get; set; } = true;
    public int PreferredSampleRate { get; set; }
    public MidiPlaybackInstrument MidiInstrument { get; set; } = MidiPlaybackInstrument.AcousticGrandPiano;
    public int DefaultVolume { get; set; } = 85;
    public bool PeakHold { get; set; } = true;
    public int VisualizerSensitivity { get; set; } = 100;
    public bool EnableVisualizerAutoCycle { get; set; } = true;
    public int VisualizerCycleSeconds { get; set; } = 12;
    public bool AutoPlayOnOpen { get; set; } = true;
    public bool QueueByDefault { get; set; }
    public bool PreserveSession { get; set; } = true;
    public bool RememberWindowPlacement { get; set; } = true;
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
    public bool ShowMoreInfo { get; set; }
    public bool EnableClipboardUrlMonitoring { get; set; } = true;
    public bool EnableDiscordRichPresence { get; set; } = true;
    public bool EnableSharedPlay { get; set; }
    public bool EnableSharedPlayLiveChannel { get; set; }
    public string SharedPlayChannelId { get; set; } = "";
    public string SharedPlayChannelOwnerToken { get; set; } = "";
    public string SharedPlayChannelDisplayName { get; set; } = "Spectralis Listener";
    public bool EnableAutoUpdates { get; set; }
    public string SharedPlayCdnBaseUrl { get; set; } = SharedPlayDefaults.CdnBaseUrl;
    public string LastSeenAppVersion { get; set; } = "";
    public string IgnoredUpdateVersion { get; set; } = "";
    public string SpotifyClientId { get; set; } = "";
    public bool EnableObsOverlay { get; set; }
    public int ObsOverlayPort { get; set; } = 5128;
    public string ObsOverlayToken { get; set; } = "";
    public string ObsOverlayPreset { get; set; } = "compact";
    public bool ObsOverlayUseDesignerProfile { get; set; }
    public bool ObsOverlayShowNowPlaying { get; set; } = true;
    public bool ObsOverlayShowLyrics { get; set; } = true;
    public bool ObsOverlayShowVisualizer { get; set; } = true;
    public bool ObsOverlayShowQueue { get; set; }
    public bool ObsOverlayShowProgress { get; set; } = true;
    public bool ObsOverlayShowNextLyric { get; set; } = true;
    public int ObsOverlayBackgroundOpacity { get; set; } = 78;
    public int ObsOverlayScale { get; set; } = 100;
    public int ObsOverlayCornerRadius { get; set; } = 10;
    public int ObsOverlayVisualizerIntensity { get; set; } = 100;
    public string ObsOverlayArtworkShape { get; set; } = "rounded";
    /// <summary>JSON-serialized ObsLayout. Empty = use legacy preset.</summary>
    public string ObsOverlayLayout { get; set; } = "";
    /// <summary>If true, custom visualizers without an obs_banner asset still appear as generic bars.</summary>
    public bool ObsOverlayAllowMissingCustomBanner { get; set; }
    /// <summary>User-saved OBS layout presets.</summary>
    public List<ObsPreset> ObsUserPresets { get; set; } = [];
    public bool ExternalApiConsentAccepted { get; set; }
    public List<string> LibraryFolders { get; set; } = [];
    public bool LibraryAutoScanOnOpen { get; set; } = true;

    // Beat Grid & BPM
    public bool AutoAnalyzeBpm    { get; set; } = true;
    public bool MetronomeEnabled  { get; set; }

    // Last.fm
    public bool   LastFmEnabled    { get; set; }
    public string LastFmApiKey     { get; set; } = "";
    public string LastFmApiSecret  { get; set; } = "";
    public string LastFmSessionKey { get; set; } = "";
    public string LastFmUsername   { get; set; } = "";

    // ListenBrainz
    public bool   ListenBrainzEnabled  { get; set; }
    public string ListenBrainzToken    { get; set; } = "";
    public string ListenBrainzUsername { get; set; } = "";

    public AppSettings Clone() =>
        new()
        {
            ThemeMode = ThemeMode,
            ThemeAccent = ThemeAccent,
            UseEmbeddedTrackThemes = UseEmbeddedTrackThemes,
            DefaultVisualizer = DefaultVisualizer,
            DefaultVisualizerKey = DefaultVisualizerKey,
            UseEmbeddedTrackVisualizers = UseEmbeddedTrackVisualizers,
            UseEmbeddedTrackContent = UseEmbeddedTrackContent,
            PreferredSampleRate = PreferredSampleRate,
            MidiInstrument = MidiInstrument,
            DefaultVolume = DefaultVolume,
            PeakHold = PeakHold,
            VisualizerSensitivity = VisualizerSensitivity,
            EnableVisualizerAutoCycle = EnableVisualizerAutoCycle,
            VisualizerCycleSeconds = VisualizerCycleSeconds,
            AutoPlayOnOpen = AutoPlayOnOpen,
            QueueByDefault = QueueByDefault,
            PreserveSession = PreserveSession,
            RememberWindowPlacement = RememberWindowPlacement,
            WindowX = WindowX,
            WindowY = WindowY,
            WindowWidth = WindowWidth,
            WindowHeight = WindowHeight,
            WindowMaximized = WindowMaximized,
            ShowMoreInfo = ShowMoreInfo,
            EnableClipboardUrlMonitoring = EnableClipboardUrlMonitoring,
            EnableDiscordRichPresence = EnableDiscordRichPresence,
            EnableSharedPlay = EnableSharedPlay,
            EnableSharedPlayLiveChannel = EnableSharedPlayLiveChannel,
            SharedPlayChannelId = SharedPlayChannelId,
            SharedPlayChannelOwnerToken = SharedPlayChannelOwnerToken,
            SharedPlayChannelDisplayName = SharedPlayChannelDisplayName,
            EnableAutoUpdates = EnableAutoUpdates,
            SharedPlayCdnBaseUrl = SharedPlayCdnBaseUrl,
            LastSeenAppVersion = LastSeenAppVersion,
            IgnoredUpdateVersion = IgnoredUpdateVersion,
            SpotifyClientId = SpotifyClientId,
            EnableObsOverlay = EnableObsOverlay,
            ObsOverlayPort = ObsOverlayPort,
            ObsOverlayToken = ObsOverlayToken,
            ObsOverlayPreset = ObsOverlayPreset,
            ObsOverlayUseDesignerProfile = ObsOverlayUseDesignerProfile,
            ObsOverlayShowNowPlaying = ObsOverlayShowNowPlaying,
            ObsOverlayShowLyrics = ObsOverlayShowLyrics,
            ObsOverlayShowVisualizer = ObsOverlayShowVisualizer,
            ObsOverlayShowQueue = ObsOverlayShowQueue,
            ObsOverlayShowProgress = ObsOverlayShowProgress,
            ObsOverlayShowNextLyric = ObsOverlayShowNextLyric,
            ObsOverlayBackgroundOpacity = ObsOverlayBackgroundOpacity,
            ObsOverlayScale = ObsOverlayScale,
            ObsOverlayCornerRadius = ObsOverlayCornerRadius,
            ObsOverlayVisualizerIntensity = ObsOverlayVisualizerIntensity,
            ObsOverlayArtworkShape = ObsOverlayArtworkShape,
            ObsOverlayLayout = ObsOverlayLayout,
            ObsOverlayAllowMissingCustomBanner = ObsOverlayAllowMissingCustomBanner,
            ObsUserPresets = ObsUserPresets.Select(p => new ObsPreset { Name = p.Name, LayoutJson = p.LayoutJson }).ToList(),
            ExternalApiConsentAccepted = ExternalApiConsentAccepted,
            LibraryFolders = LibraryFolders.ToList(),
            LibraryAutoScanOnOpen = LibraryAutoScanOnOpen,
            AutoAnalyzeBpm   = AutoAnalyzeBpm,
            MetronomeEnabled = MetronomeEnabled,
            LastFmEnabled    = LastFmEnabled,
            LastFmApiKey     = LastFmApiKey,
            LastFmApiSecret  = LastFmApiSecret,
            LastFmSessionKey = LastFmSessionKey,
            LastFmUsername   = LastFmUsername,
            ListenBrainzEnabled  = ListenBrainzEnabled,
            ListenBrainzToken    = ListenBrainzToken,
            ListenBrainzUsername = ListenBrainzUsername,
        };
}

internal sealed record SelectionOption<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

internal static class AppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private static readonly int[] AllowedSampleRates = [0, 44100, 48000, 88200, 96000];

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return Normalize(new AppSettings());

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            return Normalize(settings ?? new AppSettings());
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
            Directory.CreateDirectory(directory);

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(normalized, SerializerOptions));
    }

    public static AppSettings Normalize(AppSettings settings)
    {
        settings.ThemeMode = Enum.IsDefined(settings.ThemeMode) ? settings.ThemeMode : ThemeMode.Dark;
        settings.ThemeAccent = Enum.IsDefined(settings.ThemeAccent) ? settings.ThemeAccent : ThemeAccent.Amber;
        settings.DefaultVisualizer = Enum.IsDefined(settings.DefaultVisualizer)
            ? settings.DefaultVisualizer
            : VisualizerMode.MirrorSpectrum;
        var defaultVisualizerChoice = VisualizerChoice.FromSettingsKey(
            settings.DefaultVisualizerKey,
            settings.DefaultVisualizer);
        settings.DefaultVisualizerKey = defaultVisualizerChoice.ToSettingsKey();
        if (defaultVisualizerChoice.TryGetBuiltInMode(out var defaultVisualizerMode))
            settings.DefaultVisualizer = defaultVisualizerMode;
        settings.PreferredSampleRate = AllowedSampleRates.Contains(settings.PreferredSampleRate)
            ? settings.PreferredSampleRate
            : 0;
        settings.MidiInstrument = MidiPlaybackInstrumentCatalog.Normalize(settings.MidiInstrument);
        settings.DefaultVolume = Math.Clamp(settings.DefaultVolume, 0, 100);
        settings.VisualizerSensitivity = Math.Clamp(settings.VisualizerSensitivity, 50, 200);
        settings.VisualizerCycleSeconds = Math.Clamp(settings.VisualizerCycleSeconds, 5, 60);
        if (settings.WindowWidth < 0)
            settings.WindowWidth = 0;
        if (settings.WindowHeight < 0)
            settings.WindowHeight = 0;
        settings.SharedPlayCdnBaseUrl = SharedPlayDefaults.NormalizeCdnBaseUrl(settings.SharedPlayCdnBaseUrl);
        settings.SharedPlayChannelId = NormalizeSharedPlayChannelId(settings.SharedPlayChannelId);
        if (string.IsNullOrWhiteSpace(settings.SharedPlayChannelId))
            settings.SharedPlayChannelId = Guid.NewGuid().ToString("N")[..16];
        settings.SharedPlayChannelOwnerToken = NormalizeSharedPlayOwnerToken(settings.SharedPlayChannelOwnerToken);
        if (string.IsNullOrWhiteSpace(settings.SharedPlayChannelOwnerToken))
            settings.SharedPlayChannelOwnerToken = Guid.NewGuid().ToString("N");
        settings.SharedPlayChannelDisplayName = string.IsNullOrWhiteSpace(settings.SharedPlayChannelDisplayName)
            ? "Spectralis Listener"
            : settings.SharedPlayChannelDisplayName.Trim();
        if (settings.SharedPlayChannelDisplayName.Length > 32)
            settings.SharedPlayChannelDisplayName = settings.SharedPlayChannelDisplayName[..32];
        if (string.Equals(
            settings.SharedPlayCdnBaseUrl,
            SharedPlayDefaults.LegacyCdnBaseUrl,
            StringComparison.OrdinalIgnoreCase))
        {
            settings.SharedPlayCdnBaseUrl = SharedPlayDefaults.CdnBaseUrl;
        }
        settings.LastSeenAppVersion = string.IsNullOrWhiteSpace(settings.LastSeenAppVersion)
            ? ""
            : settings.LastSeenAppVersion.Trim();
        settings.IgnoredUpdateVersion = string.IsNullOrWhiteSpace(settings.IgnoredUpdateVersion)
            ? ""
            : settings.IgnoredUpdateVersion.Trim();
        settings.SpotifyClientId = string.IsNullOrWhiteSpace(settings.SpotifyClientId)
            ? ""
            : settings.SpotifyClientId.Trim();
        settings.ObsOverlayPort = settings.ObsOverlayPort is >= 1024 and <= 65535
            ? settings.ObsOverlayPort
            : 5128;
        if (string.IsNullOrWhiteSpace(settings.ObsOverlayToken))
            settings.ObsOverlayToken = Guid.NewGuid().ToString("N");
        settings.ObsOverlayPreset = NormalizeObsOverlayPreset(settings.ObsOverlayPreset);
        settings.ObsOverlayBackgroundOpacity = Math.Clamp(settings.ObsOverlayBackgroundOpacity, 0, 100);
        settings.ObsOverlayScale = Math.Clamp(settings.ObsOverlayScale, 75, 150);
        settings.ObsOverlayCornerRadius = Math.Clamp(settings.ObsOverlayCornerRadius, 0, 28);
        settings.ObsOverlayVisualizerIntensity = Math.Clamp(settings.ObsOverlayVisualizerIntensity, 50, 200);
        settings.ObsOverlayArtworkShape = NormalizeObsOverlayArtworkShape(settings.ObsOverlayArtworkShape);
        settings.ObsUserPresets ??= [];
        settings.ObsUserPresets.RemoveAll(p => string.IsNullOrWhiteSpace(p.Name));
        return settings;
    }

    public static string NormalizeObsOverlayPreset(string? preset)
    {
        var value = string.IsNullOrWhiteSpace(preset) ? "compact" : preset.Trim();
        return value is
            "compact" or
            "lyrics-lower-third" or
            "full-visualizer" or
            "queue-sidebar" or
            "vertical-stream" or
            "capsule-mode" or
            "minimal-ticker" or
            "album-card" or
            "lyrics-focus" or
            "visualizer-strip" or
            "stage-banner"
            ? value
            : "compact";
    }

    public static string NormalizeObsOverlayArtworkShape(string? shape)
    {
        var value = string.IsNullOrWhiteSpace(shape) ? "rounded" : shape.Trim();
        return value is "square" or "rounded" or "circle" ? value : "rounded";
    }

    private static string NormalizeSharedPlayChannelId(string? value)
    {
        var chars = (value ?? "")
            .Trim()
            .Where(static character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_')
            .Take(48)
            .ToArray();
        return new string(chars);
    }

    private static string NormalizeSharedPlayOwnerToken(string? value)
    {
        var chars = (value ?? "")
            .Trim()
            .Where(static character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.')
            .Take(96)
            .ToArray();
        return new string(chars);
    }
}
