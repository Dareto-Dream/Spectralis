using System.Diagnostics;
using System.Text;

namespace Spectralis.Core.Integrations;

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// The only sanctioned way to launch helper executables (yt-dlp, ffmpeg).
/// Arguments are always passed as an array — never a composed command line —
/// timeouts kill the whole process tree, and stdout/stderr are size-capped so
/// a hostile stream can't balloon memory. Never blocks the calling thread.
/// </summary>
public static class SafeProcessRunner
{
    public const int DefaultMaxOutputBytes = 1024 * 1024;

    public static async Task<ProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        int maxOutputBytes = DefaultMaxOutputBytes,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {Path.GetFileName(executablePath)}.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var stdoutTask = ReadBoundedAsync(process.StandardOutput, maxOutputBytes, timeoutCts.Token);
        var stderrTask = ReadBoundedAsync(process.StandardError, maxOutputBytes, timeoutCts.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            TryKill(process);
            if (!timedOut)
            {
                throw; // caller-initiated cancellation propagates
            }
        }

        string stdout, stderr;
        try
        {
            stdout = await stdoutTask;
            stderr = await stderrTask;
        }
        catch (OperationCanceledException)
        {
            stdout = string.Empty;
            stderr = string.Empty;
        }

        return new ProcessResult(timedOut ? -1 : process.ExitCode, stdout, stderr, timedOut);
    }

    /// <summary>
    /// Like <see cref="RunAsync"/> but streams stdout line-by-line to <paramref name="onStdoutLine"/>
    /// as the child writes it. Use for long-running downloads where live progress is desired.
    /// </summary>
    public static async Task<ProcessResult> RunWithProgressAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        Action<string>? onStdoutLine = null,
        int maxOutputBytes = DefaultMaxOutputBytes,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {Path.GetFileName(executablePath)}.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var stdoutBuilder = new StringBuilder();
        var stderrTask = ReadBoundedAsync(process.StandardError, maxOutputBytes, timeoutCts.Token);

        // Stream stdout line-by-line and invoke callback; also accumulate for the result.
        var stdoutTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(timeoutCts.Token)) is not null)
            {
                if (stdoutBuilder.Length < maxOutputBytes)
                    stdoutBuilder.AppendLine(line);
                try { onStdoutLine?.Invoke(line); }
                catch { /* callbacks must not crash the runner */ }
            }
            return stdoutBuilder.ToString();
        }, timeoutCts.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            TryKill(process);
            if (!timedOut) throw;
        }

        string stdout, stderr;
        try
        {
            stdout = await stdoutTask;
            stderr = await stderrTask;
        }
        catch (OperationCanceledException)
        {
            stdout = string.Empty;
            stderr = string.Empty;
        }

        return new ProcessResult(timedOut ? -1 : process.ExitCode, stdout, stderr, timedOut);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maxBytes, CancellationToken ct)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            var remaining = maxBytes - builder.Length;
            if (remaining <= 0)
            {
                // Keep draining so the child doesn't block on a full pipe, but discard.
                continue;
            }

            builder.Append(buffer, 0, Math.Min(read, remaining));
        }

        return builder.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Already exited or access denied; nothing more we can do.
        }
    }
}
