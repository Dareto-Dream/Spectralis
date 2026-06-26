using System.Diagnostics;
using System.IO;

namespace Spectralis;

internal static class YtDlpService
{
    private const string ExecutableName = "yt-dlp.exe";
    private const string BundledPayloadName = "yt-dlp.bin";

    public static string? FindExecutable()
    {
        // Bundled copy next to the executable takes priority
        var appDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (appDir is not null)
        {
            var candidate = Path.Combine(appDir, ExecutableName);
            if (File.Exists(candidate))
                return candidate;

            var bundledPayload = Path.Combine(appDir, BundledPayloadName);
            if (File.Exists(bundledPayload))
                return PrepareBundledExecutable(bundledPayload);
        }

        // Fall back to system installs
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            var candidate = Path.Combine(dir.Trim(), ExecutableName);
            if (File.Exists(candidate))
                return candidate;
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
                    return candidate;
            }
        }

        return null;
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

    public static async Task<string> GetStreamUrlAsync(string executable, string videoUrl, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-g");
        psi.ArgumentList.Add("--no-playlist");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("bestaudio[ext=m4a]/bestaudio[ext=webm]/bestaudio");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(videoUrl);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start yt-dlp");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr)
                    ? $"yt-dlp failed (exit {process.ExitCode})"
                    : stderr);

        var url = stdout.Split('\n')[0].Trim();
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("yt-dlp returned an empty URL");

        return url;
    }

    public static async Task<string?> SearchFirstVideoUrlAsync(string executable, string query, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--no-playlist");
        psi.ArgumentList.Add("--print");
        psi.ArgumentList.Add("webpage_url");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add($"ytsearch1:{query}");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start yt-dlp");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var stdout = (await stdoutTask).Trim();
        var stderr = (await stderrTask).Trim();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr)
                    ? $"yt-dlp search failed (exit {process.ExitCode})"
                    : stderr);

        return stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => line.StartsWith("http", StringComparison.OrdinalIgnoreCase));
    }
}
