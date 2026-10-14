using NAudio.Vorbis;
using NAudio.Wave;
using NLayer.NAudioSupport;

namespace Spectralis.Core.Audio;

/// <summary>Shared, seekable decoding for playback, analysis, export and stem inputs.
/// The caller owns the returned stream and any temporary decoded audio.</summary>
public static class AudioStreamFactory
{
    /// <param name="audioStreamIndex">Zero-based audio track within a container, not a
    /// channel number. Null selects the main (first) track. Use an explicit index for stems.</param>
    public static WaveStream Open(
        string path, int? audioStreamIndex = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(audioStreamIndex ?? 0);
        var extension = Path.GetExtension(path).ToLowerInvariant();

        // M4A is a container: AAC, ALAC and Opus must all reach the same decoder on
        // every platform. Selecting the track also keeps playback and export in sync
        // for multi-track files, regardless of the container's default-track flag.
        if (audioStreamIndex is not null || extension is ".m4a" or ".m4b" or ".mp4")
            return FfmpegWaveStream.Open(path, audioStreamIndex: audioStreamIndex ?? 0,
                cancellationToken: cancellationToken);

        try
        {
            switch (extension)
            {
                case ".wav": return new WaveFileReader(path);
                case ".mp3": return new Mp3FileReaderBase(path, wf => new Mp3FrameDecompressor(wf));
                case ".aif":
                case ".aifc":
                case ".aiff": return new AiffFileReader(path);
                case ".ogg":
                case ".oga": return new VorbisWaveReader(path);
            }
        }
        catch (Exception ex)
        {
            throw new NotSupportedException("The selected audio file could not be decoded by its direct reader.", ex);
        }

        var failures = new List<Exception>();
        if (OperatingSystem.IsWindows())
        {
            MediaFoundationReader? reader = null;
            try
            {
                reader = new MediaFoundationReader(path);
                if (reader.Length > 0)
                    return reader;
                throw new NotSupportedException("Media Foundation opened the file but found no decodable audio.");
            }
            catch (Exception ex)
            {
                reader?.Dispose();
                failures.Add(ex);
            }
        }

        try
        {
            return FfmpegWaveStream.Open(path, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { failures.Add(ex); }

        try
        {
            return new AudioFileReader(path);
        }
        catch (Exception ex)
        {
            failures.Add(ex);
            throw new NotSupportedException(
                "The selected audio file could not be opened. Ensure the bundled FFmpeg is available and the file contains decodable audio.",
                new AggregateException(failures));
        }
    }
}
