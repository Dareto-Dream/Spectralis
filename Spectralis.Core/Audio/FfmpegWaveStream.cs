using System.Diagnostics;
using NAudio.Wave;
using Spectralis.Core.Integrations;

namespace Spectralis.Core.Audio;

/// <summary>
/// Decodes any container ffmpeg understands into a playable <see cref="WaveStream"/>,
/// standing in for <c>MediaFoundationReader</c> off Windows. Media Foundation is a
/// Windows component, so on Linux/macOS everything without a direct NAudio reader —
/// m4a and webm especially, which is exactly what yt-dlp hands back for YouTube —
/// had nothing that could open it.
///
/// Decodes up front to a temporary WAV rather than streaming ffmpeg's stdout, because
/// <see cref="WaveStream"/> has to report an accurate <see cref="Length"/> and support
/// arbitrary seeks for the scrubber; a pipe can do neither without re-launching ffmpeg
/// on every seek. Tracks are already fully downloaded before playback, and decoding
/// runs far faster than realtime, so the wait is short.
/// </summary>
public sealed class FfmpegWaveStream : WaveStream
{
    private readonly WaveFileReader _reader;
    private readonly string _tempPath;
    private bool _disposed;

    private FfmpegWaveStream(WaveFileReader reader, string tempPath)
    {
        _reader = reader;
        _tempPath = tempPath;
    }

    public override WaveFormat WaveFormat => _reader.WaveFormat;
    public override long Length => _reader.Length;

    public override long Position
    {
        get => _reader.Position;
        set => _reader.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => _reader.Read(buffer, offset, count);

    /// <summary>
    /// Runs ffmpeg over <paramref name="path"/> and opens the result. Throws if ffmpeg
    /// is missing or can't decode the input.
    /// </summary>
    public static FfmpegWaveStream Open(string path, TimeSpan? timeout = null)
    {
        var ffmpeg = FfmpegLocator.FindExecutable()
            ?? throw new FileNotFoundException(
                "FFmpeg not found. Place 'ffmpeg' in the Spectralis application folder, " +
                "or install FFmpeg and add it to your system PATH.");

        var tempPath = Path.Combine(
            Path.GetTempPath(), $"spectralis-decode-{Guid.NewGuid():N}.wav");

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-nostdin");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(path);
        psi.ArgumentList.Add("-vn");
        // 16-bit PCM keeps the temp file half the size of float32 for no audible
        // difference; NAudio converts to float downstream anyway.
        psi.ArgumentList.Add("-acodec");
        psi.ArgumentList.Add("pcm_s16le");
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add(tempPath);

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("ffmpeg did not start.");

            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit((int)(timeout ?? TimeSpan.FromMinutes(5)).TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("ffmpeg timed out decoding the track.");
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ffmpeg could not decode this file (exit {process.ExitCode}). {stderr.Trim()}");
            }
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        try
        {
            return new FfmpegWaveStream(new WaveFileReader(tempPath), tempPath);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temp dir cleanup will get it.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing)
            {
                _reader.Dispose();
                TryDelete(_tempPath);
            }
        }

        base.Dispose(disposing);
    }
}
