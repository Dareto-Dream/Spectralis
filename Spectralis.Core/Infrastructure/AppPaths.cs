using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Spectralis.Core.Infrastructure
{
    public static class AppPaths
    {
        private static readonly string _appName = "Spectralis";

        public static string DataDirectory
        {
            get
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), _appName);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", _appName);
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", _appName.ToLower());
            }
        }

        public static string LibraryDbPath => Path.Combine(DataDirectory, "library.db");
        public static string QueuePath => Path.Combine(DataDirectory, "queue.json");
        public static string StreamingAuthPath => Path.Combine(DataDirectory, "streaming-auth.json");
        public static string StreamingHistoryPath => Path.Combine(DataDirectory, "streaming-history.json");
        public static string RecentTracksPath => Path.Combine(DataDirectory, "recent-tracks.json");
        public static string ThumbnailCacheDirectory => Path.Combine(DataDirectory, "thumbnails");
        public static string StreamingCacheDirectory => Path.Combine(DataDirectory, "streaming-cache");
        public static string PresetsDirectory => Path.Combine(DataDirectory, "presets");
        public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

        public static void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(ThumbnailCacheDirectory);
            Directory.CreateDirectory(StreamingCacheDirectory);
            Directory.CreateDirectory(PresetsDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }
    }
}
