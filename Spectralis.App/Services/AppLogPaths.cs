namespace Spectralis.App.Services;

public static class AppLogPaths
{
    /// <summary>Dev/debug logs — %TEMP%\spectralis. Easy to find, cleaned by disk cleanup.</summary>
    public static string LogDirectory =>
        Path.Combine(Path.GetTempPath(), "spectralis");

    public static string For(string fileName)
    {
        Directory.CreateDirectory(LogDirectory);
        return Path.Combine(LogDirectory, fileName);
    }

    public static void AppendTimestamped(string path, string message)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.AppendAllText(path, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}{Environment.NewLine}");
    }
}
