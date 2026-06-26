namespace Spectralis.Core.Integrations;

/// <summary>
/// yt-dlp wrapper for SoundCloud/YouTube stream resolution. All inputs are
/// untrusted: URLs must be http(s), every value travels as a discrete argument
/// after a "--" sentinel, and runs are time- and output-bounded.
/// </summary>
public static class YtDlpService
{
    private const string ExecutableName = "yt-dlp.exe";
    private const string BundledPayloadName = "yt-dlp.bin";
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(60);

    public static string? FindExecutable()
    {
        var appDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (appDir is not null)
        {
            var candidate = Path.Combine(appDir, ExecutableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var bundledPayload = Path.Combine(appDir, BundledPayloadName);
            if (File.Exists(bundledPayload))
            {
                return PrepareBundledExecutable(bundledPayload);
            }
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir.Trim(), ExecutableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var winGetPackages = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(winGetPackages))
        {
            foreach (var dir in Directory.GetDirectories(winGetPackages, "yt-dlp.yt-dlp_*"))
            {
                var candidate = Path.Combine(dir, ExecutableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>Only http/https sources may be handed to the subprocess.</summary>
    public static bool IsAllowedMediaUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
        (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    public static string[] BuildStreamUrlArguments(string videoUrl)
    {
        if (!IsAllowedMediaUrl(videoUrl))
        {
            throw new ArgumentException("Only http(s) media URLs can be resolved.", nameof(videoUrl));
        }

        return
        [
            "-g",
            "--no-playlist",
            "-f",
            "bestaudio[ext=m4a]/bestaudio[ext=webm]/bestaudio",
            "--",
            videoUrl,
        ];
    }

    public static string[] BuildSearchArguments(string query) =>
    [
        "--no-playlist",
        "--print",
        "webpage_url",
        "--",
        $"ytsearch1:{query}",
    ];

    public static async Task<string> GetStreamUrlAsync(string executable, string videoUrl, CancellationToken ct)
    {
        var result = await SafeProcessRunner.RunAsync(
            executable, BuildStreamUrlArguments(videoUrl), ResolveTimeout, cancellationToken: ct);

        if (result.TimedOut)
        {
            throw new InvalidOperationException("yt-dlp timed out resolving the stream URL.");
        }

        var stdout = result.Stdout.Trim();
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            var stderr = result.Stderr.Trim();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr) ? $"yt-dlp failed (exit {result.ExitCode})" : stderr);
        }

        var url = stdout.Split('\n')[0].Trim();
        if (!IsAllowedMediaUrl(url))
        {
            throw new InvalidOperationException("yt-dlp returned an unusable stream URL.");
        }

        return url;
    }

    public static async Task<string?> SearchFirstVideoUrlAsync(string executable, string query, CancellationToken ct)
    {
        var result = await SafeProcessRunner.RunAsync(
            executable, BuildSearchArguments(query), ResolveTimeout, cancellationToken: ct);

        if (result.TimedOut)
        {
            throw new InvalidOperationException("yt-dlp search timed out.");
        }

        if (result.ExitCode != 0)
        {
            var stderr = result.Stderr.Trim();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr) ? $"yt-dlp search failed (exit {result.ExitCode})" : stderr);
        }

        return result.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(static line => line.StartsWith("http", StringComparison.OrdinalIgnoreCase));
    }

    private static string? PrepareBundledExecutable(string payloadPath)
    {
        try
        {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spectralis",
                "tools");
            Directory.CreateDirectory(cacheDir);

            var executablePath = Path.Combine(cacheDir, ExecutableName);
            if (!File.Exists(executablePath) ||
                File.GetLastWriteTimeUtc(executablePath) != File.GetLastWriteTimeUtc(payloadPath) ||
                new FileInfo(executablePath).Length != new FileInfo(payloadPath).Length)
            {
                File.Copy(payloadPath, executablePath, overwrite: true);
                File.SetLastWriteTimeUtc(executablePath, File.GetLastWriteTimeUtc(payloadPath));
            }

            return executablePath;
        }
        catch
        {
            return null;
        }
    }
}
