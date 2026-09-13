using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Cuts signal below a threshold — useful for cleaning up quiet passages or hiss
/// between notes/phrases. Smoothed open/close envelope avoids audible clicking.
/// </summary>
public sealed class NoiseGateEffect : IAudioEffect
{
    public string Name => "Noise Gate";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("threshold", -50f);  // dBFS
        p.Set("attack", 5f);       // ms
        p.Set("release", 150f);    // ms
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new GateSampleProvider(source, Parameters);

    private sealed class GateSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;
        private float _gain;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public GateSampleProvider(ISampleProvider source, EffectParameters parameters)
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
            var threshold = _params.Get("threshold", -50f);
            var thresholdLin = (float)Math.Pow(10, threshold / 20.0);
            var attackMs = Math.Max(0.1f, _params.Get("attack", 5f));
            var releaseMs = Math.Max(1f, _params.Get("release", 150f));
            var attackCoeff = (float)Math.Exp(-1.0 / (sr * attackMs / 1000.0));
            var releaseCoeff = (float)Math.Exp(-1.0 / (sr * releaseMs / 1000.0));

            for (var i = 0; i < read; i += channels)
            {
                var peak = 0f;
                for (var c = 0; c < channels; c++)
                {
                    peak = Math.Max(peak, Math.Abs(buffer[offset + i + c]));
                }

                var target = peak >= thresholdLin ? 1f : 0f;

                _gain = target > _gain
                    ? (attackCoeff * _gain) + ((1 - attackCoeff) * target)
                    : (releaseCoeff * _gain) + ((1 - releaseCoeff) * target);

                for (var c = 0; c < channels; c++)
                {
                    buffer[offset + i + c] = Math.Clamp(buffer[offset + i + c] * _gain, -1f, 1f);
                }
            }

            return read;
        }
    }
}
