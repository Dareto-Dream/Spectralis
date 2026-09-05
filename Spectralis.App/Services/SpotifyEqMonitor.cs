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
    /// Begins capture → effects chain → playback. <paramref name="tap"/> receives the
    /// processed signal so the visualizer reflects the EQ'd audio;
    /// <paramref name="setWebViewMuted"/> silences the raw WebView output while running.
    /// </summary>
    public bool Start(int browserProcessId, EffectChain chain, VisualizerSampleProvider tap, Action<bool> setWebViewMuted)
    {
        Stop();
        try
        {
            _setWebViewMuted = setWebViewMuted;

            ISampleProvider source = new RingSampleProvider(this);
            var processed = chain.BuildChain(source);
            var monitored = new VisualizerTee(processed, tap);

            _capture = new ProcessLoopbackCapture(browserProcessId);
            _capture.Start(Write);

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
