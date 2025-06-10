using System;
using System.IO;

namespace Spectralis.Audio
{
    public static class AudioLogger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis", "audio.log");

        private static readonly object _fileLock = new object();
        private static bool _enabled = false;

        public static bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (_enabled)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    Log("Logger enabled");
                }
            }
        }

        public static void Log(string message)
        {
            if (!_enabled) return;
            lock (_fileLock)
            {
                try
                {
                    File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
                }
                catch { }
            }
        }

        public static void Log(string format, params object[] args) => Log(string.Format(format, args));

        public static void Clear()
        {
            lock (_fileLock)
            {
                try { File.Delete(LogPath); } catch { }
            }
        }
    }
}
