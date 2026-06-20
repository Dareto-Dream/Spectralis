using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Spectralis.Core.Audio.Midi;
using Spectralis.Core.Common;
using Spectralis.Core.Platform;
using Spectralis.Core.Visualizers;

namespace Spectralis.Core.Audio;

/// <summary>
/// Playback engine ported from the WinForms app: same NAudio decode chain
/// (direct readers, MediaFoundation, AudioFileReader fallback), same MIDI
/// SoundFont path, same visualizer tap — but output goes through the
/// <see cref="IAudioDevice"/> abstraction and all state flows through
/// <see cref="PlaybackStateMachine"/>.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private readonly IAudioDeviceEnumerator _deviceEnumerator;
    private readonly int _latencyMs;

    private WaveStream? _playbackStream;
    private IAudioDevice? _device;
    private VisualizerSampleProvider? _visualizer;
    private int _preferredSampleRate;
    private string? _preferredDeviceId;
    private MidiPlaybackInstrument _midiInstrument = MidiPlaybackInstrument.AcousticGrandPiano;
    private float _volume = 0.85f;
    private IEffectChainBuilder? _effectChain;
    private bool _suppressStopEvents;

    // Gapless: pre-opened stream for the next track so Load() can skip the cold-open delay.
    private WaveStream? _preparedStream;
    private string? _preparedPath;

    public AudioEngine(IAudioDeviceEnumerator? deviceEnumerator = null, int latencyMs = 70)
    {
        _deviceEnumerator = deviceEnumerator ?? new WaveOutDeviceEnumerator();
        _latencyMs = latencyMs;
    }

    public PlaybackStateMachine StateMachine { get; } = new();

    public TrackInfo? CurrentTrack { get; private set; }

    public bool IsLoaded => _playbackStream is not null && _device is not null;

    public bool IsPlaying => _device?.IsPlaying == true;

    public bool IsMidiLoaded => _playbackStream is MidiPlaybackStream;

    public int EffectiveSampleRate =>
        _visualizer?.WaveFormat.SampleRate ?? _playbackStream?.WaveFormat.SampleRate ?? 0;

    /// <summary>Raised when the loaded track plays to its natural end.</summary>
    public event EventHandler? TrackEnded;

    /// <summary>Raised when the output device failed and could not be recovered.</summary>
    public event EventHandler? DeviceRecoveryFailed;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            if (_device is not null)
            {
                _device.Volume = _volume;
            }
        }
    }

    public void Load(string path, TrackInfo? providedInfo = null)
    {
        DisposePlayback();
        StateMachine.TryTransitionTo(PlaybackState.Loading);

        try
        {
            // Use the pre-opened stream if it matches; otherwise open normally.
            WaveStream? prepared = null;
            if (string.Equals(_preparedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                prepared = Interlocked.Exchange(ref _preparedStream, null);
                _preparedPath = null;
            }

            string formatName;
            if (prepared is not null)
            {
                _playbackStream = prepared;
                formatName = GetContainerLabel(Path.GetExtension(path).ToLowerInvariant(), "audio");
            }
            else
            {
                _playbackStream = OpenPlaybackStream(path, out formatName);
            }
            CurrentTrack = BuildTrackInfo(path, _playbackStream, formatName, providedInfo);
            CreateOutputChain(TimeSpan.Zero, resumePlayback: false);
            StateMachine.TransitionTo(PlaybackState.Stopped);
        }
        catch (Exception ex)
        {
            DisposePlayback(toIdle: false);
            StateMachine.TryTransitionTo(PlaybackState.Error, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Pre-opens the audio stream for <paramref name="path"/> on a background thread so that
    /// a subsequent <see cref="Load"/> call for the same path can use it without a cold-open delay.
    /// Safe to call while a track is playing. No-op for MIDI or unsupported formats.
    /// </summary>
    public async Task PrepareNextAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        // Drop stale prepared stream first.
        var old = Interlocked.Exchange(ref _preparedStream, null);
        _preparedPath = null;
        old?.Dispose();

        try
        {
            var stream = await Task.Run(() => OpenPlaybackStream(path, out _));
            _preparedStream = stream;
            _preparedPath = path;
        }
        catch
        {
            // Preparation is best-effort; failure is silent — Load() will open normally.
        }
    }

    /// <summary>
    /// Attempts a seamless advance to <paramref name="nextPath"/> without tearing down the audio device.
    /// The currently-playing sample source is swapped under the device while it continues to run.
    /// Returns true when the seamless swap succeeded; false causes the caller to fall back to a normal Load().
    /// </summary>
    public bool TrySeamlessAdvance(string nextPath, TrackInfo? providedInfo = null)
    {
        if (_device is null || _playbackStream is null || !IsLoaded)
            return false;

        WaveStream? nextStream = null;

        // Prefer the prepared stream if it matches.
        if (string.Equals(_preparedPath, nextPath, StringComparison.OrdinalIgnoreCase) && _preparedStream is not null)
        {
            nextStream = Interlocked.Exchange(ref _preparedStream, null);
            _preparedPath = null;
        }

        if (nextStream is null)
        {
            try { nextStream = OpenPlaybackStream(nextPath, out _); }
            catch { return false; }
        }

        try
        {
            var wasPlaying = _device.IsPlaying;
            _suppressStopEvents = true;
            _device.Stop();
            _suppressStopEvents = false;

            _playbackStream.Dispose();
            _playbackStream = nextStream;
            CurrentTrack = BuildTrackInfo(nextPath, nextStream, string.Empty, providedInfo);

            // Re-wire the visualizer and effect chain onto the new stream.
            ISampleProvider src = nextStream.ToSampleProvider();
            if (_preferredSampleRate > 0 && src.WaveFormat.SampleRate != _preferredSampleRate)
                src = new WdlResamplingSampleProvider(src, _preferredSampleRate);
            if (_effectChain is not null)
                src = _effectChain.BuildChain(src);
            _visualizer = new VisualizerSampleProvider(src);

            _device.Init(new SampleProviderSource(_visualizer));
            if (wasPlaying)
            {
                _device.Play();
                StateMachine.TryTransitionTo(PlaybackState.Playing);
            }
            else
            {
                StateMachine.TryTransitionTo(PlaybackState.Stopped);
            }
            return true;
        }
        catch
        {
            nextStream.Dispose();
            return false;
        }
        finally
        {
            _suppressStopEvents = false;
        }
    }

    public void Unload()
    {
        DisposePlayback();
        StateMachine.TryTransitionTo(PlaybackState.Stopped);
        StateMachine.TryTransitionTo(PlaybackState.Idle);
    }

    public void Toggle()
    {
        if (!IsLoaded)
        {
            return;
        }

        if (IsPlaying)
            Pause();
        else
            Play();
    }

    public void Play()
    {
        if (!IsLoaded || _device is null || _playbackStream is null)
        {
            return;
        }

        if (!_device.IsPlaying &&
            _playbackStream.TotalTime > TimeSpan.Zero &&
            _playbackStream.CurrentTime >= _playbackStream.TotalTime)
        {
            _playbackStream.CurrentTime = TimeSpan.Zero;
        }

        try
        {
            _device.Play();
            StateMachine.TryTransitionTo(PlaybackState.Playing);
        }
        catch (Exception)
        {
            TryRecoverAudioDevice();
        }
    }

    public void Pause()
    {
        if (!IsLoaded || _device is null)
        {
            return;
        }

        try
        {
            if (_device.IsPlaying)
            {
                _device.Pause();
                StateMachine.TryTransitionTo(PlaybackState.Paused);
            }
        }
        catch (Exception)
        {
            TryRecoverAudioDevice();
        }
    }

    public void Stop()
    {
        if (!IsLoaded || _device is null || _playbackStream is null)
        {
            return;
        }

        _suppressStopEvents = true;
        try
        {
            _device.Stop();
        }
        finally
        {
            _suppressStopEvents = false;
        }

        _playbackStream.CurrentTime = TimeSpan.Zero;
        _visualizer?.Clear();
        StateMachine.TryTransitionTo(PlaybackState.Stopped);
    }

    public void Seek(float seconds)
    {
        if (_playbackStream is null)
        {
            return;
        }

        var clampedSeconds = Math.Clamp(seconds, 0, GetLength());
        _playbackStream.CurrentTime = TimeSpan.FromSeconds(clampedSeconds);
    }

    public float GetPosition() => (float)(_playbackStream?.CurrentTime.TotalSeconds ?? 0);

    public float GetLength() => (float)(_playbackStream?.TotalTime.TotalSeconds ?? 0);

    /// <summary>When set, GetVisualizerFrame reads from this source instead of the playback chain.
    /// Used by Spotify loopback so the visualizer shows Spotify audio while the engine is idle.</summary>
    public VisualizerSampleProvider? ExternalVisualizerSource { get; set; }

    public VisualizerFrame GetVisualizerFrame(bool includeSpectrogram = false, bool includeRawFft = false)
    {
        var frame = (ExternalVisualizerSource ?? _visualizer)?.GetFrame(includeSpectrogram, includeRawFft) ?? VisualizerFrame.Empty;
        return _playbackStream is MidiPlaybackStream midiStream
            ? frame with
            {
                MidiNotes = midiStream.GetActiveNotes(),
                MidiInstrumentName = midiStream.InstrumentDisplayName,
            }
            : frame;
    }

    public void SetEffectChain(IEffectChainBuilder? chain)
    {
        _effectChain = chain;
        RebuildEffectChain();
    }

    public void RebuildEffectChain()
    {
        if (!IsLoaded || _playbackStream is null || _device is null)
        {
            return;
        }

        var pos = _playbackStream.CurrentTime;
        var wasPlaying = _device.IsPlaying;
        CreateOutputChain(pos, wasPlaying);
    }

    public void SetPreferredSampleRate(int sampleRate)
    {
        var normalized = Math.Max(0, sampleRate);
        if (_preferredSampleRate == normalized)
        {
            return;
        }

        _preferredSampleRate = normalized;
        if (!IsLoaded || _playbackStream is null || _device is null)
        {
            return;
        }

        CreateOutputChain(_playbackStream.CurrentTime, _device.IsPlaying);
    }

    public void SetOutputDevice(string? deviceId)
    {
        if (_preferredDeviceId == deviceId)
        {
            return;
        }

        _preferredDeviceId = deviceId;
        if (!IsLoaded || _playbackStream is null || _device is null)
        {
            return;
        }

        CreateOutputChain(_playbackStream.CurrentTime, _device.IsPlaying);
    }

    public void SetMidiPlaybackInstrument(MidiPlaybackInstrument instrument)
    {
        var normalized = MidiPlaybackInstrumentCatalog.Normalize(instrument);
        if (_midiInstrument == normalized)
        {
            return;
        }

        _midiInstrument = normalized;
        if (_playbackStream is not MidiPlaybackStream || CurrentTrack is null)
        {
            return;
        }

        // MIDI is rendered offline through the SoundFont, so an instrument change
        // requires a re-render; resume at the same position.
        var currentPosition = _playbackStream.CurrentTime;
        var resumePlayback = _device?.IsPlaying == true;
        var trackInfo = CurrentTrack;

        _suppressStopEvents = true;
        try
        {
            _device?.Stop();
            _device?.Dispose();
        }
        finally
        {
            _suppressStopEvents = false;
        }

        _playbackStream.Dispose();
        _device = null;
        _visualizer = null;

        _playbackStream = OpenPlaybackStream(trackInfo.SourcePath, out var formatName);
        CurrentTrack = trackInfo with
        {
            FormatName = formatName,
            Channels = Math.Max(1, _playbackStream.WaveFormat.Channels),
            SampleRateHz = _playbackStream.WaveFormat.SampleRate,
            Duration = _playbackStream.TotalTime,
        };
        CreateOutputChain(currentPosition, resumePlayback);
    }

    public void Dispose()
    {
        var prepared = Interlocked.Exchange(ref _preparedStream, null);
        _preparedPath = null;
        prepared?.Dispose();
        DisposePlayback();
    }

    private void CreateOutputChain(TimeSpan currentPosition, bool resumePlayback)
    {
        if (_playbackStream is null)
        {
            return;
        }

        _device?.Dispose();
        _device = null;

        _playbackStream.CurrentTime = currentPosition;

        ISampleProvider sampleProvider = _playbackStream.ToSampleProvider();
        if (_preferredSampleRate > 0 && sampleProvider.WaveFormat.SampleRate != _preferredSampleRate)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, _preferredSampleRate);
        }

        if (_effectChain is not null)
        {
            sampleProvider = _effectChain.BuildChain(sampleProvider);
        }

        _visualizer = new VisualizerSampleProvider(sampleProvider);
        _device = _deviceEnumerator.CreateDevice(_preferredDeviceId, _latencyMs);
        _device.Volume = _volume;
        _device.PlaybackStopped += OnDevicePlaybackStopped;
        _device.Init(new SampleProviderSource(_visualizer));

        if (resumePlayback)
        {
            _device.Play();
            StateMachine.TryTransitionTo(PlaybackState.Playing);
        }
    }

    private void OnDevicePlaybackStopped(object? sender, AudioDeviceStoppedEventArgs e)
    {
        if (_suppressStopEvents || !ReferenceEquals(sender, _device))
        {
            return;
        }

        if (e.Exception is not null)
        {
            TryRecoverAudioDevice();
            return;
        }

        // Natural end of stream: the device drained after the source returned 0.
        if (StateMachine.State == PlaybackState.Playing &&
            _playbackStream is not null &&
            _playbackStream.TotalTime > TimeSpan.Zero &&
            _playbackStream.CurrentTime >= _playbackStream.TotalTime - TimeSpan.FromMilliseconds(250))
        {
            StateMachine.TryTransitionTo(PlaybackState.Stopped);
            TrackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private static TrackInfo BuildTrackInfo(
        string path,
        WaveStream stream,
        string formatName,
        TrackInfo? provided)
    {
        long fileSize = 0;
        try
        {
            fileSize = new FileInfo(path).Length;
        }
        catch
        {
            // remote/virtual sources have no local size
        }

        var baseInfo = provided ?? new TrackInfo { SourcePath = path };
        return baseInfo with
        {
            SourcePath = path,
            FormatName = string.IsNullOrWhiteSpace(baseInfo.FormatName) ? formatName : baseInfo.FormatName,
            Channels = Math.Max(1, stream.WaveFormat.Channels),
            SampleRateHz = stream.WaveFormat.SampleRate,
            FileSizeBytes = fileSize > 0 ? fileSize : baseInfo.FileSizeBytes,
            Duration = ResolveProvidedTrackDuration(baseInfo, stream.TotalTime),
        };
    }

    private static TimeSpan ResolveProvidedTrackDuration(TrackInfo trackInfo, TimeSpan decoderDuration)
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
                    return new MidiPlaybackStream(path, MidiSoundFontLocator.ResolveDefaultSoundFontPath(), _midiInstrument);
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
                    "The selected audio file could not be opened. This app supports many common formats directly and additional formats through installed system codecs.",
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
            _ => fallbackLabel,
        };

    private void TryRecoverAudioDevice()
    {
        if (_playbackStream is null)
        {
            return;
        }

        try
        {
            var currentPosition = _playbackStream.CurrentTime;
            var wasPlaying = _device?.IsPlaying == true;
            CreateOutputChain(currentPosition, resumePlayback: wasPlaying);
        }
        catch
        {
            _device?.Dispose();
            _device = null;
            StateMachine.TryTransitionTo(PlaybackState.Error, "Audio device recovery failed.");
            DeviceRecoveryFailed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DisposePlayback(bool toIdle = true)
    {
        _suppressStopEvents = true;
        try
        {
            _device?.Stop();
            _device?.Dispose();
        }
        finally
        {
            _suppressStopEvents = false;
        }

        _playbackStream?.Dispose();
        _device = null;
        _playbackStream = null;
        _visualizer = null;
        CurrentTrack = null;
    }

    /// <summary>Adapts an NAudio sample provider to the device-facing source interface.</summary>
    private sealed class SampleProviderSource : IAudioSampleSource
    {
        private readonly ISampleProvider _provider;

        public SampleProviderSource(ISampleProvider provider) => _provider = provider;

        public int SampleRate => _provider.WaveFormat.SampleRate;
        public int Channels => _provider.WaveFormat.Channels;
        public int Read(float[] buffer, int offset, int count) => _provider.Read(buffer, offset, count);
    }
}
