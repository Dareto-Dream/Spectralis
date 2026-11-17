namespace Spectralis.App.Services;

/// <summary>Only portable preferences cross devices. Credentials, paths, devices and update policy stay local.</summary>
public sealed record WardPreferences(int SchemaVersion, int ThemeMode, int ThemeAccent,
    bool ShowVisualizer, bool PeakHold, int VisualizerSensitivity, bool EnableClipboardUrlMonitoring,
    bool QueueByDefault, bool GaplessPlayback, int CrossfadeSeconds)
{
    public static WardPreferences Capture(AppSettings settings) => new(1, (int)settings.ThemeMode, (int)settings.ThemeAccent,
        settings.ShowVisualizer, settings.PeakHold, settings.VisualizerSensitivity, settings.EnableClipboardUrlMonitoring,
        settings.QueueByDefault, settings.GaplessPlayback, settings.CrossfadeSeconds);

    public void Apply(AppSettings settings)
    {
        if (SchemaVersion != 1 || !Enum.IsDefined(typeof(AppThemeMode), ThemeMode) || !Enum.IsDefined(typeof(AppThemeAccent), ThemeAccent)
            || VisualizerSensitivity is < 0 or > 200 || CrossfadeSeconds is < 0 or > 12)
            throw new InvalidOperationException("These saved settings are not supported by this version of Spectralis.");
        settings.ThemeMode = (AppThemeMode)ThemeMode;
        settings.ThemeAccent = (AppThemeAccent)ThemeAccent;
        settings.ShowVisualizer = ShowVisualizer;
        settings.PeakHold = PeakHold;
        settings.VisualizerSensitivity = VisualizerSensitivity;
        settings.EnableClipboardUrlMonitoring = EnableClipboardUrlMonitoring;
        settings.QueueByDefault = QueueByDefault;
        settings.GaplessPlayback = GaplessPlayback;
        settings.CrossfadeSeconds = CrossfadeSeconds;
    }
}
