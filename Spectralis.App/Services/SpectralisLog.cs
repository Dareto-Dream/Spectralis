using System.Diagnostics;

namespace Spectralis.App.Services;

public static class SpectralisLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Sync = new();

    public static string LogPath => AppLogPaths.For("spectralis.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? exception = null) =>
        Write("ERR ", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.Now:HH:mm:ss.fff zzz} [{level}] {message}";
        Trace.WriteLine(line, "Spectralis");

        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(AppLogPaths.LogDirectory);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                {
                    File.WriteAllText(LogPath, line + Environment.NewLine);
                }
                else
                {
                    File.AppendAllText(LogPath, line + Environment.NewLine);
                }
            }
        }
        catch
        {
            // Logging must never break playback or app startup.
        }
    }
}
