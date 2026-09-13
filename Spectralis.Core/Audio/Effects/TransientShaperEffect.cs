using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Punch/attack control independent of overall level: tracks a fast and a slow
/// envelope, uses their difference as a transient measure, and blends between
/// an "attack" gain (applied during transients) and a "sustain" gain (applied
/// to the steady-state portion).
/// </summary>
public sealed class TransientShaperEffect : IAudioEffect
{
    public string Name => "Transient Shaper";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("attack", 6f);   // dB, -24 to 24
        p.Set("sustain", 0f);  // dB, -24 to 24
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new ShaperSampleProvider(source, Parameters);

    private sealed class ShaperSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;
        private float _fastEnv;
        private float _slowEnv;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public ShaperSampleProvider(ISampleProvider source, EffectParameters parameters)
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
            var attackDb = Math.Clamp(_params.Get("attack", 6f), -24f, 24f);
            var sustainDb = Math.Clamp(_params.Get("sustain", 0f), -24f, 24f);

            var fastAttack = (float)Math.Exp(-1.0 / (sr * 1.0 / 1000.0));
            var fastRelease = (float)Math.Exp(-1.0 / (sr * 15.0 / 1000.0));
            var slowAttack = (float)Math.Exp(-1.0 / (sr * 30.0 / 1000.0));
            var slowRelease = (float)Math.Exp(-1.0 / (sr * 120.0 / 1000.0));

            for (var i = 0; i < read; i += channels)
            {
                var peak = 0f;
                for (var c = 0; c < channels; c++)
                {
                    peak = Math.Max(peak, Math.Abs(buffer[offset + i + c]));
                }

                _fastEnv = peak > _fastEnv
                    ? (fastAttack * _fastEnv) + ((1 - fastAttack) * peak)
                    : (fastRelease * _fastEnv) + ((1 - fastRelease) * peak);

                _slowEnv = peak > _slowEnv
                    ? (slowAttack * _slowEnv) + ((1 - slowAttack) * peak)
                    : (slowRelease * _slowEnv) + ((1 - slowRelease) * peak);

                var transientAmount = _fastEnv > 1e-6f
                    ? Math.Clamp((_fastEnv - _slowEnv) / _fastEnv, 0f, 1f)
                    : 0f;

                var gainDb = (attackDb * transientAmount) + (sustainDb * (1f - transientAmount));
                var gain = (float)Math.Pow(10, gainDb / 20.0);

                for (var c = 0; c < channels; c++)
                {
                    buffer[offset + i + c] = Math.Clamp(buffer[offset + i + c] * gain, -1f, 1f);
                }
            }

            return read;
        }
    }
}
