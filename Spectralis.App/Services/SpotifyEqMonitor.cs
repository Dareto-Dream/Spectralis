using NAudio.Wave;
using Spectralis.Core.Audio.Effects;
using Spectralis.Core.Audio.Loopback;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Services;

/// <summary>
/// Experimental: captures the Spotify WebView's process audio, runs it through the
/// shared effects chain, and re-outputs it — so the parametric EQ (and the rest of
/// the rack) reaches Spotify, whose Web Playback SDK otherwise streams DRM audio
/// straight past NAudio. Windows 10 20348+ only; adds output latency. Opt-in via
/// <c>AppSettings.EqSpotifyAudioExperimental</c>.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class SpotifyEqMonitor : IDisposable
{
    private const int SampleRate = 44100;
    private const int Channels = 2;

    private readonly object _sync = new();
    private readonly float[] _ring = new float[SampleRate * Channels]; // ~1 s headroom
    private int _writePos;
    private int _available;

    private ProcessLoopbackCapture? _capture;
    private IWavePlayer? _output;
    private Action<bool>? _setWebViewMuted;

    public static bool IsSupported => ProcessLoopbackCapture.IsSupported;

    public bool IsRunning { get; private set; }

    public string? Status { get; private set; }

    /// <summary>
    /// Begins capture → effects chain → playback. <paramref name="captureProcessId"/> must be a
    /// process the WebView's audio is a descendant of, NOT the WebView2 browser sub-process
    /// itself — activating process-loopback directly against that sandboxed/broker process
    /// reliably fails with E_ILLEGAL_METHOD_CALL ("a method was called at an unexpected time"),
    /// even on a clean attempt well after audio is confirmed playing (so it isn't a startup
    /// race). Pass the hosting .NET process id instead; <see cref="ProcessLoopbackCapture"/>'s
    /// IncludeTargetProcessTree mode recursively covers the WebView's descendants from there —
    /// the same target <see cref="Spectralis.Core.Audio.Loopback.WindowsLoopbackCaptureSource"/>'s
    /// plain tap already uses successfully. <paramref name="tap"/> receives the processed signal
    /// so the visualizer reflects the EQ'd audio; <paramref name="setWebViewMuted"/> silences the
    /// raw WebView output while running. Runs on the caller's thread (the UI/WebView2
    /// message-dispatch thread) — must await <see cref="ProcessLoopbackCapture.StartAsync"/>
    /// rather than its blocking <c>Start</c> wrapper, or a still-in-flight activation can be
    /// reentered by the very next WebView2 message. See that method's doc comment for the full
    /// mechanism.
    /// </summary>
    public async Task<bool> StartAsync(int captureProcessId, EffectChain chain, VisualizerSampleProvider tap, Action<bool> setWebViewMuted)
    {
        Stop();
        try
        {
            _setWebViewMuted = setWebViewMuted;

            ISampleProvider source = new RingSampleProvider(this);
            var processed = chain.BuildChain(source);
            var monitored = new VisualizerTee(processed, tap);

            _capture = new ProcessLoopbackCapture(captureProcessId);
            await _capture.StartAsync(Write);

            _output = new WaveOutEvent { DesiredLatency = 120, NumberOfBuffers = 3 };
            _output.Init(monitored);
            _output.Play();

            setWebViewMuted(true);
            IsRunning = true;
            Status = "running";
            return true;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        IsRunning = false;
        try { _setWebViewMuted?.Invoke(false); } catch { }
        _setWebViewMuted = null;

        _output?.Dispose();
        _output = null;
        _capture?.Dispose();
        _capture = null;

        lock (_sync)
        {
            _writePos = 0;
            _available = 0;
            Array.Clear(_ring);
        }
    }

    public void Dispose() => Stop();

    private void Write(float[] buffer, int offset, int count, int channels)
    {
        lock (_sync)
        {
            for (var i = 0; i < count; i++)
            {
                _ring[_writePos] = buffer[offset + i];
                _writePos = (_writePos + 1) % _ring.Length;
                if (_available < _ring.Length)
                {
                    _available++;
                }

                // On overrun the oldest samples are simply overwritten and _available stays capped.
            }
        }
    }

    private int ReadRing(float[] buffer, int offset, int count)
    {
        lock (_sync)
        {
            var n = Math.Min(count, _available);
            var readPos = (((_writePos - _available) % _ring.Length) + _ring.Length) % _ring.Length;
            for (var i = 0; i < n; i++)
            {
                buffer[offset + i] = _ring[readPos];
                readPos = (readPos + 1) % _ring.Length;
            }

            _available -= n;

            // Pad with silence so the output device never starves.
            for (var i = n; i < count; i++)
            {
                buffer[offset + i] = 0f;
            }

            return count;
        }
    }

    private sealed class RingSampleProvider(SpotifyEqMonitor owner) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

        public int Read(float[] buffer, int offset, int count) => owner.ReadRing(buffer, offset, count);
    }

    private sealed class VisualizerTee(ISampleProvider source, VisualizerSampleProvider tap) : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, count);
            if (read > 0)
            {
                tap.FeedExternalSamples(buffer, offset, read, Math.Max(1, WaveFormat.Channels));
            }

            return read;
        }
    }
}
