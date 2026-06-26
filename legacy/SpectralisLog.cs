using System.Diagnostics;
using System.IO;

namespace Spectralis;

internal static class SpectralisLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Spectralis",
        "spectralis.log");

    private static readonly object Lock = new();
    private const long MaxBytes = 512 * 1024; // 512 KB rolling cap

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERR ", message);

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}";
        Trace.WriteLine(line, "Spectralis");

        try
        {
            lock (Lock)
            {
                var dir = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(dir);

                // Trim log file when it exceeds the cap
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                    File.WriteAllText(LogPath, line + Environment.NewLine);
                else
                    File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch { }
    }
}
