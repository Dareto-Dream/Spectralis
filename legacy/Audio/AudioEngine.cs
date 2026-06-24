using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Spectralis;

public sealed class AudioEngine : IDisposable
{
    private WaveStream? playbackStream;
    private WaveOutEvent? output;
    private VisualizerSampleProvider? visualizer;
    private int preferredSampleRate;
    private MidiPlaybackInstrument midiInstrument = MidiPlaybackInstrument.AcousticGrandPiano;
    private float volume = 0.85f;
    private EffectChain? effectChain;

    public event EventHandler? DeviceRecoveryFailed;

    public AudioTrackInfo? CurrentTrack { get; private set; }

    public bool IsLoaded => playbackStream is not null && output is not null;

    public bool IsPlaying => output?.PlaybackState == PlaybackState.Playing;

    public int EffectiveSampleRate => visualizer?.WaveFormat.SampleRate ?? playbackStream?.WaveFormat.SampleRate ?? 0;

    public bool IsMidiLoaded => playbackStream is MidiPlaybackStream;

    public float Volume
    {
        get => volume;
        set
        {
            volume = Math.Clamp(value, 0, 1);

            if (output is not null)
            {
                output.Volume = volume;
            }
        }
    }

    public void Load(string path)
    {
        DisposePlayback();

        var metadata = AudioMetadataReader.Read(path);
        playbackStream = OpenPlaybackStream(path, out var formatName);
        CurrentTrack = BuildTrackInfo(path, playbackStream, formatName, metadata);
        CreateOutputChain(TimeSpan.Zero, resumePlayback: false);
    }

    public void Load(string path, AudioTrackInfo trackInfo)
    {
        DisposePlayback();

        playbackStream = OpenPlaybackStream(path, out var formatName);
        CurrentTrack = trackInfo with
        {
            FormatName = string.IsNullOrWhiteSpace(trackInfo.FormatName) ? formatName : trackInfo.FormatName,
            Channels = Math.Max(1, playbackStream.WaveFormat.Channels),
            SourceSampleRate = playbackStream.WaveFormat.SampleRate,
            BitsPerSample = playbackStream.WaveFormat.BitsPerSample,
            Duration = ResolveProvidedTrackDuration(trackInfo, playbackStream.TotalTime)
        };
        CreateOutputChain(TimeSpan.Zero, resumePlayback: false);
    }

    internal void SetMidiPlaybackInstrument(MidiPlaybackInstrument instrument)
    {
        var normalizedInstrument = MidiPlaybackInstrumentCatalog.Normalize(instrument);
        if (midiInstrument == normalizedInstrument)
            return;

        midiInstrument = normalizedInstrument;

        if (playbackStream is not MidiPlaybackStream || CurrentTrack is null)
            return;

        var currentPosition = playbackStream.CurrentTime;
        var resumePlayback = output?.PlaybackState == PlaybackState.Playing;
        var trackInfo = CurrentTrack;

        output?.Stop();
        output?.Dispose();
        playbackStream.Dispose();
        output = null;
        visualizer = null;

        playbackStream = OpenPlaybackStream(trackInfo.FilePath, out var formatName);
        CurrentTrack = trackInfo with
        {
            FormatName = formatName,
            Channels = Math.Max(1, playbackStream.WaveFormat.Channels),
            SourceSampleRate = playbackStream.WaveFormat.SampleRate,
            BitsPerSample = playbackStream.WaveFormat.BitsPerSample,
            Duration = playbackStream.TotalTime
        };
        CreateOutputChain(currentPosition, resumePlayback);
    }

    public void TakePlaybackFrom(AudioEngine prepared)
    {
        if (ReferenceEquals(this, prepared))
            return;

        if (prepared.playbackStream is null || prepared.output is null)
            throw new InvalidOperationException("The prepared audio engine is not loaded.");

        DisposePlayback();

        playbackStream = prepared.playbackStream;
        output = prepared.output;
        visualizer = prepared.visualizer;
        CurrentTrack = prepared.CurrentTrack;

        prepared.playbackStream = null;
        prepared.output = null;
        prepared.visualizer = null;
        prepared.CurrentTrack = null;

        if (output is not null)
            output.Volume = volume;
    }

    public void Unload() => DisposePlayback();

    public void Toggle()
    {
        if (!IsLoaded || output is null || playbackStream is null)
        {
            return;
        }

        if (output.PlaybackState == PlaybackState.Playing)
            Pause();
        else
            Play();
    }

    public void Play()
    {
        if (!IsLoaded || output is null || playbackStream is null)
        {
            return;
        }

        if (output.PlaybackState == PlaybackState.Stopped &&
            playbackStream.TotalTime > TimeSpan.Zero &&
            playbackStream.CurrentTime >= playbackStream.TotalTime)
        {
            playbackStream.CurrentTime = TimeSpan.Zero;
        }

        try
        {
            output.Play();
        }
        catch (NAudio.MmException)
        {
            TryRecoverAudioDevice();
        }
    }

    public void Pause()
    {
        if (!IsLoaded || output is null)
        {
            return;
        }

        try
        {
            if (output.PlaybackState == PlaybackState.Playing)
                output.Pause();
        }
        catch (NAudio.MmException)
        {
            TryRecoverAudioDevice();
        }
    }

    public void Stop()
    {
        if (!IsLoaded || output is null || playbackStream is null)
        {
            return;
        }

        output.Stop();
        playbackStream.CurrentTime = TimeSpan.Zero;
        visualizer?.Clear();
    }

    internal void SetEffectChain(EffectChain? chain)
    {
        effectChain = chain;
        RebuildEffectChain();
    }

    internal void RebuildEffectChain()
    {
        if (!IsLoaded || playbackStream is null || output is null) return;
        var pos         = playbackStream.CurrentTime;
        var wasPlaying  = output.PlaybackState == PlaybackState.Playing;
        CreateOutputChain(pos, wasPlaying);
    }

    public void SetPreferredSampleRate(int sampleRate)
    {
        var normalizedSampleRate = Math.Max(0, sampleRate);
        if (preferredSampleRate == normalizedSampleRate)
        {
            return;
        }

        preferredSampleRate = normalizedSampleRate;

        if (!IsLoaded || playbackStream is null || output is null)
        {
            return;
        }

        var currentPosition = playbackStream.CurrentTime;
        var resumePlayback = output.PlaybackState == PlaybackState.Playing;
        CreateOutputChain(currentPosition, resumePlayback);
    }

    public void Seek(float seconds)
    {
        if (playbackStream is null)
        {
            return;
        }

        var clampedSeconds = Math.Clamp(seconds, 0, GetLength());
        playbackStream.CurrentTime = TimeSpan.FromSeconds(clampedSeconds);
    }

    public float GetPosition() => (float)(playbackStream?.CurrentTime.TotalSeconds ?? 0);

    public float GetLength() => (float)(playbackStream?.TotalTime.TotalSeconds ?? 0);

    public VisualizerFrame GetVisualizerFrame()
    {
        var frame = visualizer?.GetFrame() ?? VisualizerFrame.Empty;
        return playbackStream is MidiPlaybackStream midiStream
            ? frame with
            {
                MidiNotes = midiStream.GetActiveNotes(),
                MidiInstrumentName = midiStream.InstrumentDisplayName
            }
            : frame;
    }

    public void Dispose()
    {
        DisposePlayback();
        GC.SuppressFinalize(this);
    }

    private void CreateOutputChain(TimeSpan currentPosition, bool resumePlayback)
    {
        if (playbackStream is null)
        {
            return;
        }

        output?.Dispose();
        output = null;

        playbackStream.CurrentTime = currentPosition;

        ISampleProvider sampleProvider = playbackStream.ToSampleProvider();
        if (preferredSampleRate > 0 && sampleProvider.WaveFormat.SampleRate != preferredSampleRate)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, preferredSampleRate);
        }

        if (effectChain is not null)
            sampleProvider = effectChain.BuildChain(sampleProvider);

        visualizer = new VisualizerSampleProvider(sampleProvider);
        output = new WaveOutEvent
        {
            DesiredLatency = 70,
            NumberOfBuffers = 3,
            Volume = volume
        };
        output.Init(visualizer);

        if (resumePlayback)
        {
            output.Play();
        }
    }

    private static AudioTrackInfo BuildTrackInfo(
        string path,
        WaveStream stream,
        string formatName,
        AudioFileMetadata metadata)
    {
        var fallbackDisplayName = Path.GetFileNameWithoutExtension(path);
        var displayName = FirstNonEmpty(metadata.Title, fallbackDisplayName, Path.GetFileName(path));

        return new AudioTrackInfo(
            path,
            string.IsNullOrWhiteSpace(displayName) ? Path.GetFileName(path) : displayName,
            metadata.Artist,
            metadata.Album,
            metadata.AlbumArtBytes,
            metadata.Lyrics,
            metadata.EmbeddedVisualizer,
            metadata.EmbeddedTheme,
            metadata.EmbeddedHtml,
            metadata.EmbeddedMarkdown,
            metadata.EmbeddedVideo,
            formatName,
            Math.Max(1, stream.WaveFormat.Channels),
            stream.WaveFormat.SampleRate,
            stream.WaveFormat.BitsPerSample,
            stream.TotalTime);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static TimeSpan ResolveProvidedTrackDuration(AudioTrackInfo trackInfo, TimeSpan decoderDuration)
    {
        if (string.Equals(trackInfo.FormatName, "Spectralis Capsule", StringComparison.OrdinalIgnoreCase) &&
            trackInfo.Duration > TimeSpan.Zero)
        {
            return trackInfo.Duration;
        }

        return decoderDuration > TimeSpan.Zero ? decoderDuration : trackInfo.Duration;
    }

    private WaveStream OpenPlaybackStream(string path, out string formatName)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        try
        {
            switch (extension)
            {
                case ".mid":
                case ".midi":
                case ".kar":
                    formatName = "MIDI";
                    return new MidiPlaybackStream(path, MidiSoundFontLocator.ResolveDefaultSoundFontPath(), midiInstrument);
                case ".wav":
                    formatName = "WAV";
                    return new WaveFileReader(path);
                case ".mp3":
                    formatName = "MP3";
                    return new Mp3FileReader(path);
                case ".aif":
                case ".aifc":
                case ".aiff":
                    formatName = "AIFF";
                    return new AiffFileReader(path);
                case ".ogg":
                case ".oga":
                    formatName = "Ogg Vorbis";
                    return new VorbisWaveReader(path);
            }
        }
        catch (Exception directReaderException)
        {
            throw new NotSupportedException("The selected audio file could not be decoded by its direct reader.", directReaderException);
        }

        try
        {
            formatName = GetContainerLabel(extension, "Windows codec");
            return new MediaFoundationReader(path);
        }
        catch (Exception mediaFoundationException)
        {
            try
            {
                formatName = GetContainerLabel(extension, "NAudio fallback");
                return new AudioFileReader(path);
            }
            catch (Exception fallbackException)
            {
                throw new NotSupportedException(
                    "The selected audio file could not be opened. This app supports many common formats directly and additional formats through installed Windows codecs.",
                    new AggregateException(mediaFoundationException, fallbackException));
            }
        }
    }

    private static string GetContainerLabel(string extension, string fallbackLabel) =>
        extension switch
        {
            ".aac" => "AAC",
            ".adts" => "AAC / ADTS",
            ".asf" => "ASF",
            ".flac" => "FLAC",
            ".m4a" => "M4A",
            ".m4b" => "M4B",
            ".m4p" => "M4P",
            ".mp4" => "MP4 audio",
            ".opus" => "Opus",
            ".webm" => "WebM audio",
            ".wma" => "WMA",
            ".3gp" => "3GP audio",
            _ => fallbackLabel
        };

    private void TryRecoverAudioDevice()
    {
        if (playbackStream is null)
        {
            return;
        }

        try
        {
            var currentPosition = playbackStream.CurrentTime;
            var wasPlaying = output?.PlaybackState == PlaybackState.Playing;
            CreateOutputChain(currentPosition, resumePlayback: wasPlaying);
        }
        catch
        {
            // Device recovery failed - notify UI and dispose
            output?.Dispose();
            output = null;
            DeviceRecoveryFailed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DisposePlayback()
    {
        output?.Stop();
        output?.Dispose();
        playbackStream?.Dispose();
        output = null;
        playbackStream = null;
        visualizer = null;
        CurrentTrack = null;
    }
}
