using System.Collections.Generic;

namespace Spectralis.Core.Settings
{
    public class AppSettings
    {
        public float Volume { get; set; } = 0.8f;
        public bool StartMinimized { get; set; }
        public bool MinimizeToTray { get; set; } = true;
        public bool RememberWindowSize { get; set; } = true;
        public double WindowWidth { get; set; } = 1200;
        public double WindowHeight { get; set; } = 780;
        public bool ShowLibraryOnStart { get; set; } = true;
        public bool ShowQueueOnStart { get; set; } = true;
        public string LastOpenDirectory { get; set; } = string.Empty;
        public List<string> WatchFolders { get; set; } = new();
        public bool ScanOnStartup { get; set; }
        public string LastFmUsername { get; set; } = string.Empty;
        public bool ScrobblingEnabled { get; set; }
        public bool ReplayGainEnabled { get; set; }
        public float ReplayGainPreamp { get; set; }
        public string SpotifyClientId { get; set; } = string.Empty;
        public string YouTubeApiKey { get; set; } = string.Empty;
        public string SoundCloudClientId { get; set; } = string.Empty;
        public string YtDlpPath { get; set; } = string.Empty;
        public int CoverArtCacheLimitMb { get; set; } = 512;
        public string Theme { get; set; } = "dark";
        public string AccentColor { get; set; } = "#7B68EE";
    }
}
