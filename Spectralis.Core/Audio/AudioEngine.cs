using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLayer.NAudioSupport;
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
    // Short gain ramp applied on every device start/stop boundary (track transitions, device
    // recovery after another app grabs the shared audio device, manual play/pause) so playback
    // never begins or ends on a hard, arbitrary-phase sample — which is what produces an audible
    // click/pop through a shared-mode Windows audio device.
    private const int FadeMs = 25;

    private readonly IAudioDeviceEnumerator _deviceEnumerator;
    private readonly int _latencyMs;

    private WaveStream? _playbackStream;
    private IAudioDevice? _device;
    private VisualizerSampleProvider? _visualizer;
    private FadeInOutSampleProvider? _fade;
    private int _preferredSampleRate;
    private string? _preferredDeviceId;
    private MidiPlaybackInstrument _midiInstrument = MidiPlaybackInstrument.AcousticGrandPiano;
    private float _volume = 0.85f;
    private double _playbackRate = 1.0;
    private IEffectChainBuilder? _effectChain;
    private bool _suppressStopEvents;

    // Gapless: pre-opened stream for the next track so Load() can skip the cold-open delay.
    private WaveStream? _preparedStream;
    private string? _preparedPath;

    // In-chain transitions (true gapless / crossfade): the head of the sample chain swaps to the
    // prepared track on its own, so the output device never stops between tracks.
    private TransitionSampleProvider? _transition;
    private WaveStream? _outgoingStream;
    private string? _transitionedPath;
    private long _transitionedAtTick;
    private bool _gaplessEnabled = true;
    private double _crossfadeSeconds;
    private const long TransitionClaimWindowMs = 5000;

    public AudioEngine(IAudioDeviceEnumerator? deviceEnumerator = null, int latencyMs = 70)
    {
        _deviceEnumerator = deviceEnumerator ?? AudioDeviceEnumeratorFactory.Create();
        _latencyMs = latencyMs;
    }

    /// <summary>The platform backend used to list and open output devices.</summary>
    public IAudioDeviceEnumerator DeviceEnumerator => _deviceEnumerator;

    public PlaybackStateMachine StateMachine { get; } = new();

    public TrackInfo? CurrentTrack { get; private set; }

    public bool IsLoaded => _playbackStream is not null && _device is not null;

    public bool IsPlaying => _device?.IsPlaying == true;

    public bool IsMidiLoaded => _playbackStream is MidiPlaybackStream;

    public int EffectiveSampleRate =>
        _visualizer?.WaveFormat.SampleRate ?? _playbackStream?.WaveFormat.SampleRate ?? 0;

    /// <summary>Raised when the loaded track plays to its natural end.</summary>
    public event EventHandler? TrackEnded;

    /// <summary>
    /// Raised (audio thread) when playback moved to the prepared next track inside the sample
    /// chain — at the splice for gapless, at the start of the overlap for a crossfade. The argument
    /// is the new track's path; <see cref="CurrentTrack"/> already points at it.
    /// </summary>
    public event EventHandler<string>? TrackTransitioned;

    /// <summary>Raised when the output device failed and could not be recovered.</summary>
    public event EventHandler? DeviceRecoveryFailed;

    /// <summary>Join tracks with no gap when the next one is prepared. On by default.</summary>
    public bool GaplessEnabled
    {
        get => _gaplessEnabled;
        set
        {
            if (_gaplessEnabled == value)
                return;
            _gaplessEnabled = value;
            ReapplyTransitionSettings();
        }
    }

    /// <summary>Crossfade length in seconds; 0 turns crossfade off (clamped to 0–12).</summary>
    public double CrossfadeSeconds
    {
        get => _crossfadeSeconds;
        set
        {
            var clamped = Math.Clamp(value, 0, 12);
            if (Math.Abs(_crossfadeSeconds - clamped) < 1e-6)
                return;
            _crossfadeSeconds = clamped;
            ReapplyTransitionSettings();
        }
    }

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

        // Drop stale prepared stream first (and make sure the chain isn't still pointing at it).
        _transition?.Disarm();
        var old = Interlocked.Exchange(ref _preparedStream, null);
        _preparedPath = null;
        old?.Dispose();

        try
        {
            var stream = await Task.Run(() => OpenPlaybackStream(path, out _));
            _preparedStream = stream;
            _preparedPath = path;
            TryArmTransition();
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

        // The chain already moved to this track by itself (gapless/crossfade): nothing to swap,
        // just reconcile the metadata the caller resolved for it.
        if (ClaimTransitionedTrack(nextPath))
        {
            CurrentTrack = BuildTrackInfo(nextPath, _playbackStream, string.Empty, providedInfo ?? CurrentTrack);
            return true;
        }

        _transition?.Disarm();
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

            if (wasPlaying && _fade is not null)
            {
                // Ramp the outgoing track to silence and give the device a moment to actually
                // play those faded samples before Stop() cuts the stream — otherwise Stop() can
                // land mid-waveform on an arbitrary sample, which is audible as a click.
                _fade.BeginFadeOut(FadeMs);
                Thread.Sleep(FadeMs + 10);
            }

            _suppressStopEvents = true;
            _device.Stop();
            _suppressStopEvents = false;

            _playbackStream.Dispose();
            Interlocked.Exchange(ref _outgoingStream, null)?.Dispose();
            _playbackStream = nextStream;
            CurrentTrack = BuildTrackInfo(nextPath, nextStream, string.Empty, providedInfo);

            // Re-wire the speed/resample/effect chain and visualizer onto the new stream.
            _visualizer = new VisualizerSampleProvider(BuildTransitionHead(nextStream))
            {
                RawBlockCaptured = RawAudioBlockCaptured,
            };
            _fade = new FadeInOutSampleProvider(_visualizer, initiallySilent: true);

            _device.Init(new SampleProviderSource(_fade));
            if (wasPlaying)
            {
                _fade.BeginFadeIn(FadeMs);
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

        var wasPlaying = _device.IsPlaying;

        if (!wasPlaying &&
            _playbackStream.TotalTime > TimeSpan.Zero &&
            _playbackStream.CurrentTime >= _playbackStream.TotalTime)
        {
            _playbackStream.CurrentTime = TimeSpan.Zero;
        }

        try
        {
            // Only (re)trigger the ramp when actually starting from a stopped/paused state —
            // calling BeginFadeIn while already playing would restart the ramp and dip the volume.
            if (!wasPlaying)
                _fade?.BeginFadeIn(FadeMs);
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

    /// <summary>
    /// Optional tap for the raw post-EffectChain interleaved samples of every audio block —
    /// wired onto <see cref="VisualizerSampleProvider.RawBlockCaptured"/> every time the
    /// visualizer/fade chain is (re)built, so it survives track changes, effect-chain rebuilds,
    /// and seamless-advance transitions without callers needing to re-set it. Used by Satellite
    /// to stream what the listener is actually hearing (DSP already applied) to paired
    /// receivers. Set once; null by default, so nothing changes for callers that never use it.
    /// </summary>
    public Action<float[], int, int, int>? RawAudioBlockCaptured { get; set; }

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

    /// <summary>The current pitch-preserving playback speed (1.0 = normal). Podcast Mode uses this.</summary>
    public double PlaybackRate => _playbackRate;

    /// <summary>
    /// Sets a pitch-preserving playback speed. Clamped to [0.5, 3.5]; a no-op for MIDI
    /// (offline-rendered through the SoundFont). Rebuilds the output chain at the current
    /// position — same pattern as <see cref="SetPreferredSampleRate"/>.
    /// </summary>
    public void SetPlaybackRate(double rate)
    {
        var normalized = Math.Clamp(rate, 0.5, 3.5);
        if (Math.Abs(_playbackRate - normalized) < 1e-4)
        {
            return;
        }

        _playbackRate = normalized;
        if (!IsLoaded || _playbackStream is null || _device is null || _playbackStream is MidiPlaybackStream)
        {
            return;
        }

        CreateOutputChain(_playbackStream.CurrentTime, _device.IsPlaying);
    }

    /// <summary>
    /// Ramps the output to silence over <paramref name="ms"/> then pauses — the click-free
    /// stop used by the sleep timer. A subsequent <see cref="Play"/> fades back in normally.
    /// </summary>
    public async Task FadeOutAndPause(int ms)
    {
        if (!IsLoaded || _device is null)
        {
            return;
        }

        _fade?.BeginFadeOut(Math.Max(1, ms));
        await Task.Delay(Math.Max(1, ms) + 20).ConfigureAwait(false);
        Pause();
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

    /// <summary>
    /// Builds the sample pipeline shared by <see cref="CreateOutputChain"/> and
    /// <see cref="TrySeamlessAdvance"/>: speed control → preferred-rate resample → effect chain.
    /// The visualizer tap and fade wrap the result.
    /// </summary>
    private ISampleProvider BuildProcessedProvider(WaveStream stream)
    {
        ISampleProvider provider = stream.ToSampleProvider();

        if (_playbackRate != 1.0 && stream is not MidiPlaybackStream)
        {
            provider = new VariableSpeedSampleProvider(provider, _playbackRate);
        }

        if (_preferredSampleRate > 0 && provider.WaveFormat.SampleRate != _preferredSampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, _preferredSampleRate);
        }

        if (_effectChain is not null)
        {
            provider = _effectChain.BuildChain(provider);
        }

        return provider;
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

        _visualizer = new VisualizerSampleProvider(BuildTransitionHead(_playbackStream))
        {
            RawBlockCaptured = RawAudioBlockCaptured,
        };
        _fade = new FadeInOutSampleProvider(_visualizer, initiallySilent: true);
        _device = _deviceEnumerator.CreateDevice(_preferredDeviceId, _latencyMs);
        _device.Volume = _volume;
        _device.PlaybackStopped += OnDevicePlaybackStopped;
        _device.Init(new SampleProviderSource(_fade));

        if (resumePlayback)
        {
            // Also covers device-recovery after another app grabs the shared audio device
            // (TryRecoverAudioDevice resumes through here) — a fresh device negotiating with
            // another app's playback is exactly when a hard-start click is most audible.
            _fade.BeginFadeIn(FadeMs);
            _device.Play();
            StateMachine.TryTransitionTo(PlaybackState.Playing);
        }

        // A rebuilt chain (rate/effects/device change) lost any armed transition; re-arm it.
        TryArmTransition();
    }

    // ── In-chain transitions ──────────────────────────────────────────────────

    /// <summary>Wraps the processed provider for <paramref name="stream"/> in a fresh transition head.</summary>
    private ISampleProvider BuildTransitionHead(WaveStream stream)
    {
        if (_transition is not null)
        {
            _transition.Transitioning -= OnChainTransitioning;
            _transition.OutgoingFinished -= OnChainOutgoingFinished;
        }

        _transition = new TransitionSampleProvider(BuildProcessedProvider(stream));
        _transition.Transitioning += OnChainTransitioning;
        _transition.OutgoingFinished += OnChainOutgoingFinished;
        return _transition;
    }

    private void ReapplyTransitionSettings()
    {
        _transition?.Disarm();
        TryArmTransition();
    }

    /// <summary>
    /// Queues the prepared next track into the chain when gapless or crossfade is on. Skipped for
    /// MIDI (offline-rendered, different timing) and when the next track's channel layout can't be
    /// matched to the running chain — those fall back to the normal advance.
    /// </summary>
    private void TryArmTransition()
    {
        var transition = _transition;
        var current = _playbackStream;
        var next = _preparedStream;
        if (transition is null || current is null || next is null || transition.IsArmed)
            return;
        if (!_gaplessEnabled && _crossfadeSeconds <= 0)
            return;
        if (current is MidiPlaybackStream || next is MidiPlaybackStream)
            return;

        var matched = MatchFormat(BuildProcessedProvider(next), transition.WaveFormat);
        if (matched is null)
            return;

        var rate = Math.Max(0.1, _playbackRate);
        var overlap = 0.0;
        if (_crossfadeSeconds > 0)
        {
            // Never overlap more than half of either track.
            overlap = Math.Min(_crossfadeSeconds, Math.Min(current.TotalTime.TotalSeconds, next.TotalTime.TotalSeconds) / 2);
            if (overlap < 0.25)
                overlap = 0;
        }

        if (overlap <= 0 && !_gaplessEnabled)
            return;

        transition.QueueNext(matched, overlap, () => (current.TotalTime - current.CurrentTime).TotalSeconds / rate);
    }

    private static ISampleProvider? MatchFormat(ISampleProvider provider, WaveFormat target)
    {
        if (provider.WaveFormat.SampleRate != target.SampleRate)
            provider = new WdlResamplingSampleProvider(provider, target.SampleRate);

        var channels = provider.WaveFormat.Channels;
        if (channels == target.Channels)
            return provider;
        if (channels == 1 && target.Channels == 2)
            return new MonoToStereoSampleProvider(provider);
        if (channels == 2 && target.Channels == 1)
            return new StereoToMonoSampleProvider(provider);
        return null;
    }

    // Audio thread: the chain just started playing the prepared track.
    private void OnChainTransitioning()
    {
        var next = Interlocked.Exchange(ref _preparedStream, null);
        var path = _preparedPath;
        _preparedPath = null;
        if (next is null || path is null)
            return;

        _outgoingStream = _playbackStream;
        _playbackStream = next;
        CurrentTrack = BuildTrackInfo(path, next, GetContainerLabel(Path.GetExtension(path).ToLowerInvariant(), "audio"), null);
        _transitionedPath = path;
        _transitionedAtTick = Environment.TickCount64;
        TrackTransitioned?.Invoke(this, path);
    }

    // Audio thread: the old track has been fully mixed out; free its decoder off the audio thread.
    private void OnChainOutgoingFinished()
    {
        var old = Interlocked.Exchange(ref _outgoingStream, null);
        if (old is not null)
            ThreadPool.QueueUserWorkItem(_ => { try { old.Dispose(); } catch { } });
    }

    /// <summary>True once, shortly after the chain transitioned into <paramref name="path"/> by itself.</summary>
    private bool ClaimTransitionedTrack(string path)
    {
        var claimed = _transitionedPath;
        if (claimed is null ||
            !string.Equals(claimed, path, StringComparison.OrdinalIgnoreCase) ||
            Environment.TickCount64 - _transitionedAtTick > TransitionClaimWindowMs)
        {
            return false;
        }

        _transitionedPath = null;
        return true;
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
                    // Mp3FileReader's default frame decompressor calls into Windows' ACM
                    // codec (msacm32.dll), which doesn't exist on Linux/macOS. NLayer is a
                    // pure managed MP3 decoder, so it works the same on every platform.
                    return new Mp3FileReaderBase(path, wf => new Mp3FrameDecompressor(wf));
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

        // Media Foundation is a Windows component. Off Windows the bundled ffmpeg is
        // the general-purpose decoder — without it nothing here could open the m4a and
        // webm that yt-dlp returns for YouTube.
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                formatName = GetContainerLabel(extension, "FFmpeg");
                return FfmpegWaveStream.Open(path);
            }
            catch (Exception ffmpegException)
            {
                try
                {
                    formatName = GetContainerLabel(extension, "NAudio fallback");
                    return new AudioFileReader(path);
                }
                catch (Exception fallbackException)
                {
                    throw new NotSupportedException(
                        "The selected audio file could not be opened. Playing this format needs the bundled FFmpeg, which is missing or could not decode it.",
                        new AggregateException(ffmpegException, fallbackException));
                }
            }
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
        Interlocked.Exchange(ref _outgoingStream, null)?.Dispose();
        if (_transition is not null)
        {
            _transition.Transitioning -= OnChainTransitioning;
            _transition.OutgoingFinished -= OnChainOutgoingFinished;
            _transition = null;
        }
        _transitionedPath = null;
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
