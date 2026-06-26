using System.IO;

namespace Spectralis;

public static class AppLogPaths
{
    private static string LogDirectory => Path.Combine(Path.GetTempPath(), "spectralis");

    public static string For(string fileName)
    {
        Directory.CreateDirectory(LogDirectory);
        return Path.Combine(LogDirectory, fileName);
    }

    public static void AppendTimestamped(string path, string message)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
    }
}
