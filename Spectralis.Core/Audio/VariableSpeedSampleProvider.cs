using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Spectralis.Core.Audio;

/// <summary>
/// Plays a source at a wall-clock speed factor (<see cref="Rate"/>) while preserving pitch —
/// the "1.5× podcast" control. Immutable: a rate change rebuilds the instance (the underlying
/// <see cref="WdlResamplingSampleProvider"/> has no live ratio setter).
///
/// Chain for a source at Fs Hz: pitch-shift <em>up</em> by <c>Rate</c> (so the later resample
/// cancels it), then resample Fs → Fs/Rate, then relabel the output back to Fs. Reporting the
/// original sample rate is what makes the audio device consume the stream faster — and keeps
/// <see cref="AudioEngine.EffectiveSampleRate"/>, the visualizer FFT, and the rate readout
/// unaffected.
///
/// <see cref="SmbPitchShiftingSampleProvider"/> is hard-coded mono/stereo, so sources with more
/// than two channels fall back to a plain passthrough (as does <c>Rate == 1</c>).
/// </summary>
public sealed class VariableSpeedSampleProvider : ISampleProvider
{
    private const double Epsilon = 1e-4;

    private readonly ISampleProvider _head;

    public VariableSpeedSampleProvider(ISampleProvider source, double rate)
    {
        Rate = rate;
        var sourceFormat = source.WaveFormat;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sourceFormat.SampleRate, sourceFormat.Channels);

        if (Math.Abs(rate - 1.0) < Epsilon || sourceFormat.Channels > 2)
        {
            _head = source;
            return;
        }

        var pitched = new SmbPitchShiftingSampleProvider(source)
        {
            PitchFactor = (float)(1.0 / rate),
        };
        _head = new WdlResamplingSampleProvider(
            pitched,
            (int)Math.Round(sourceFormat.SampleRate / rate));
    }

    /// <summary>The wall-clock speed factor this instance was built for.</summary>
    public double Rate { get; }

    /// <summary>Always the source's format — the resample is deliberately hidden from callers.</summary>
    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count) => _head.Read(buffer, offset, count);
}
