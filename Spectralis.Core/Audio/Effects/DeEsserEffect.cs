using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Targeted sibilance reduction: splits off the high band above <c>frequency</c>,
/// compresses only that band when it crosses <c>threshold</c>, and recombines with
/// the untouched low band. Mainly useful if capsules ever carry vocal-heavy content.
/// </summary>
public sealed class DeEsserEffect : IAudioEffect
{
    public string Name => "De-esser";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("frequency", 6500f);  // Hz, sibilance band start
        p.Set("threshold", -20f);   // dBFS
        p.Set("reduction", 0.6f);   // 0-1, strength of gain reduction above threshold
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new DeEsserSampleProvider(source, Parameters);

    private sealed class DeEsserSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;
        private readonly Biquad[] _bandFilters;
        private float _gain = 1f;
        private float _builtFrequency = float.NaN;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public DeEsserSampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
            var channels = Math.Max(1, source.WaveFormat.Channels);
            _bandFilters = new Biquad[channels];
            for (var c = 0; c < channels; c++)
            {
                _bandFilters[c] = new Biquad();
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read == 0)
            {
                return 0;
            }

            var sr = _source.WaveFormat.SampleRate;
            var channels = _bandFilters.Length;
            var frequency = Math.Clamp(_params.Get("frequency", 6500f), 1000f, (sr / 2f) - 100f);
            var threshold = _params.Get("threshold", -20f);
            var thresholdLin = (float)Math.Pow(10, threshold / 20.0);
            var reduction = Math.Clamp(_params.Get("reduction", 0.6f), 0f, 1f);

            if (Math.Abs(frequency - _builtFrequency) > 1f)
            {
                foreach (var f in _bandFilters)
                {
                    f.SetCoefficients(EqFilterType.HighPass, sr, frequency, 0.707, 0);
                }

                _builtFrequency = frequency;
            }

            // Fast attack / moderate release, similar time constants to CompressorEffect.
            var attackCoeff = (float)Math.Exp(-1.0 / (sr * 3.0 / 1000.0));
            var releaseCoeff = (float)Math.Exp(-1.0 / (sr * 80.0 / 1000.0));

            for (var i = 0; i < read; i += channels)
            {
                var bandPeak = 0f;
                var bandSamples = new float[channels];
                for (var c = 0; c < channels; c++)
                {
                    var band = _bandFilters[c].Process(buffer[offset + i + c]);
                    bandSamples[c] = band;
                    bandPeak = Math.Max(bandPeak, Math.Abs(band));
                }

                var target = bandPeak > thresholdLin && bandPeak > 1e-9f
                    ? 1f - (reduction * (1f - (thresholdLin / bandPeak)))
                    : 1f;
                target = Math.Clamp(target, 0f, 1f);

                _gain = target < _gain
                    ? (attackCoeff * _gain) + ((1 - attackCoeff) * target)
                    : (releaseCoeff * _gain) + ((1 - releaseCoeff) * target);

                for (var c = 0; c < channels; c++)
                {
                    var full = buffer[offset + i + c];
                    var low = full - bandSamples[c];
                    buffer[offset + i + c] = Math.Clamp(low + (bandSamples[c] * _gain), -1f, 1f);
                }
            }

            return read;
        }
    }
}
