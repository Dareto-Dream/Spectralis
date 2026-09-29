namespace Spectralis.App.Services;

public static class AppLogPaths
{
    /// <summary>Dev/debug logs — %TEMP%\spectralis. Easy to find, cleaned by disk cleanup.</summary>
    public static string LogDirectory =>
        Path.Combine(Path.GetTempPath(), "spectralis");

    // File.AppendAllText opens, writes, and closes the file per call rather than holding a
    // handle — fine for one caller, but two threads logging to the same path at once (e.g.
    // WasmWorldLog: the render thread logs roughly once a second, the UI thread logs on every
    // key/pointer event) can race and hit a sharing-violation IOException on whichever call
    // loses. That exception is unhandled on whatever background thread wrote it, which crashes
    // the whole process — confirmed: took down a real session mid-drag. One lock across every
    // log file is simpler than a per-path lock table and these writes are tiny/infrequent enough
    // that serializing them costs nothing worth measuring.
    private static readonly object Lock = new();

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

        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {message}{Environment.NewLine}";
        lock (Lock)
        {
            File.AppendAllText(path, line);
        }
    }
}
