namespace Spectralis.Core.Integrations;

/// <summary>
/// Finds the ffmpeg binary: the copy bundled next to the app first, then PATH.
/// Same order (and the same executable-bit repair) as
/// <see cref="YtDlpService.FindExecutable"/> uses for yt-dlp.
/// </summary>
public static class FfmpegLocator
{
    // The bundled/PATH binary has no ".exe" suffix outside Windows — searching
    // for "ffmpeg.exe" on Linux/macOS never matches a real ffmpeg install.
    private static string ExecutableName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    public static string? FindExecutable()
    {
        var appDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (appDir is not null)
        {
            var bundled = Path.Combine(appDir, ExecutableName);
            if (File.Exists(bundled))
            {
                EnsureExecutable(bundled);
                return bundled;
            }
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, ExecutableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Unreadable PATH entry; keep looking.
            }
        }

        return null;
    }

    /// <summary>Re-asserts the executable bit on the bundled binary (git tracks it
    /// explicitly since this repo is cross-published from Windows) so a stray build
    /// step or plain copy deploy can't silently break launch.</summary>
    public static void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            const UnixFileMode exec =
                UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            var mode = File.GetUnixFileMode(path);
            if ((mode & exec) != exec)
            {
                File.SetUnixFileMode(path, mode | exec);
            }
        }
        catch
        {
            // Best-effort; Process.Start surfaces a clear error if it really isn't runnable.
        }
    }
}
