using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Brickwall peak limiter: near-instant gain reduction whenever the signal would
/// exceed the ceiling, with a smoothed release. Natural pairing after
/// <see cref="CompressorEffect"/> — the compressor handles overall dynamics,
/// this just catches whatever peaks slip through.
/// </summary>
public sealed class LimiterEffect : IAudioEffect
{
    public string Name => "Limiter";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("ceiling", -0.3f);  // dBFS
        p.Set("release", 50f);    // ms
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new LimiterSampleProvider(source, Parameters);

    private sealed class LimiterSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;
        private float _gain = 1f;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public LimiterSampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read == 0)
            {
                return 0;
            }

            var sr = _source.WaveFormat.SampleRate;
            var channels = Math.Max(1, _source.WaveFormat.Channels);
            var ceilingDb = Math.Min(0f, _params.Get("ceiling", -0.3f));
            var ceiling = (float)Math.Pow(10, ceilingDb / 20.0);
            var releaseMs = Math.Max(1f, _params.Get("release", 50f));
            var releaseCoeff = (float)Math.Exp(-1.0 / (sr * releaseMs / 1000.0));

            for (var i = 0; i < read; i += channels)
            {
                var peak = 0f;
                for (var c = 0; c < channels; c++)
                {
                    peak = Math.Max(peak, Math.Abs(buffer[offset + i + c]));
                }

                var neededGain = peak > 1e-9f ? Math.Min(1f, ceiling / peak) : 1f;

                _gain = neededGain < _gain
                    ? neededGain                                          // instant attack
                    : (releaseCoeff * _gain) + ((1 - releaseCoeff) * neededGain);

                for (var c = 0; c < channels; c++)
                {
                    buffer[offset + i + c] = Math.Clamp(buffer[offset + i + c] * _gain, -1f, 1f);
                }
            }

            return read;
        }
    }
}
