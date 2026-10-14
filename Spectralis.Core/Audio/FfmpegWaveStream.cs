using System.Globalization;
using NAudio.Wave;
using Spectralis.Core.Integrations;

namespace Spectralis.Core.Audio;

/// <summary>
/// Decodes a selected audio track to a temporary float WAV. The result supports
/// accurate duration and random seeks for playback, analysis and stem mixing,
/// without reducing lossless M4A to 16-bit or restarting FFmpeg on every seek.
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

    internal string DecodedPath => _tempPath;

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
    public static FfmpegWaveStream Open(
        string path, TimeSpan? timeout = null, int audioStreamIndex = 0,
        CancellationToken cancellationToken = default) =>
        // Playback still has a synchronous load API. Run the async process helper
        // without capturing the UI context while that API waits for the decoder.
        Task.Run(() => OpenAsync(path, timeout, audioStreamIndex, cancellationToken))
            .GetAwaiter().GetResult();

    public static async Task<FfmpegWaveStream> OpenAsync(
        string path, TimeSpan? timeout = null, int audioStreamIndex = 0,
        CancellationToken cancellationToken = default, int? sampleRate = null, int? channels = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(audioStreamIndex);
        var ffmpeg = FfmpegLocator.FindExecutable()
            ?? throw new FileNotFoundException(
                "FFmpeg not found. Place FFmpeg in the Spectralis application folder, " +
                "or install FFmpeg and add it to your system PATH.");

        var tempPath = Path.Combine(
            Path.GetTempPath(), $"spectralis-decode-{Guid.NewGuid():N}.wav");

        try
        {
            var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-i", path,
                "-map", "0:a:" + audioStreamIndex.ToString(CultureInfo.InvariantCulture), "-vn", "-acodec", "pcm_f32le" };
            if (sampleRate is { } rate) { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rate); arguments.AddRange(["-ar", rate.ToString(CultureInfo.InvariantCulture)]); }
            if (channels is { } count) { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count); arguments.AddRange(["-ac", count.ToString(CultureInfo.InvariantCulture)]); }
            arguments.AddRange(["-y", tempPath]);
            var result = await SafeProcessRunner.RunAsync(ffmpeg, arguments,
                timeout ?? TimeSpan.FromMinutes(5), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.TimedOut)
                throw new TimeoutException("FFmpeg timed out decoding the track.");
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"FFmpeg could not decode audio track {audioStreamIndex} (exit {result.ExitCode}). {result.Stderr.Trim()}");

            var reader = new WaveFileReader(tempPath);
            if (reader.Length == 0)
            {
                reader.Dispose();
                throw new NotSupportedException("FFmpeg found no decodable audio in the selected track.");
            }
            return new FfmpegWaveStream(reader, tempPath);
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
