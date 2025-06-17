using System;

namespace Spectralis.Core.Infrastructure
{
    public enum SpectralLogLevel { Debug, Info, Warning, Error, Critical }

    public interface ISpectralLogger
    {
        void Log(SpectralLogLevel level, string category, string message, Exception? ex = null);
        void Debug(string category, string message) => Log(SpectralLogLevel.Debug, category, message);
        void Info(string category, string message) => Log(SpectralLogLevel.Info, category, message);
        void Warning(string category, string message, Exception? ex = null) => Log(SpectralLogLevel.Warning, category, message, ex);
        void Error(string category, string message, Exception? ex = null) => Log(SpectralLogLevel.Error, category, message, ex);
    }

    public class FileLogger : ISpectralLogger
    {
        private readonly string _path;
        private readonly object _lock = new object();

        public FileLogger(string path) => _path = path;

        public void Log(SpectralLogLevel level, string category, string message, Exception? ex = null)
        {
            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{category}] {message}";
            if (ex != null) line += Environment.NewLine + ex.ToString();

            lock (_lock)
            {
                try { System.IO.File.AppendAllText(_path, line + Environment.NewLine); }
                catch { }
            }
        }
    }

    public class NullLogger : ISpectralLogger
    {
        public static readonly NullLogger Instance = new NullLogger();
        public void Log(SpectralLogLevel level, string category, string message, Exception? ex = null) { }
    }
}
