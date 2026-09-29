using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Three-band compressor: splits the signal at two crossover points (additive
/// low/high split, mid = remainder) and compresses each band independently before
/// summing back together. More "mastering-grade" control than the single-band
/// <see cref="CompressorEffect"/>, at the cost of more knobs.
/// </summary>
public sealed class MultibandCompressorEffect : IAudioEffect
{
    public string Name => "Multiband Compressor";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("lowXover", 150f);
        p.Set("highXover", 3000f);
        p.Set("lowThreshold", -18f);
        p.Set("lowRatio", 3f);
        p.Set("midThreshold", -18f);
        p.Set("midRatio", 3f);
        p.Set("highThreshold", -18f);
        p.Set("highRatio", 3f);
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new MultibandSampleProvider(source, Parameters);

    private sealed class MultibandSampleProvider : ISampleProvider
    {
        private const float AttackMs = 10f;
        private const float ReleaseMs = 150f;

        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;
        private readonly Biquad[] _lowFilters;
        private readonly Biquad[] _highFilters;
        private float _lowGainDb;
        private float _midGainDb;
        private float _highGainDb;
        private float _builtLowXover = float.NaN;
        private float _builtHighXover = float.NaN;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public MultibandSampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
            var channels = Math.Max(1, source.WaveFormat.Channels);
            _lowFilters = new Biquad[channels];
            _highFilters = new Biquad[channels];
            for (var c = 0; c < channels; c++)
            {
                _lowFilters[c] = new Biquad();
                _highFilters[c] = new Biquad();
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
            var channels = _lowFilters.Length;

            var lowXover = Math.Clamp(_params.Get("lowXover", 150f), 20f, 1000f);
            var highXover = Math.Max(lowXover + 100f, Math.Clamp(_params.Get("highXover", 3000f), 500f, (sr / 2f) - 100f));

            if (Math.Abs(lowXover - _builtLowXover) > 1f || Math.Abs(highXover - _builtHighXover) > 1f)
            {
                for (var c = 0; c < channels; c++)
                {
                    _lowFilters[c].SetCoefficients(EqFilterType.LowPass, sr, lowXover, 0.707, 0);
                    _highFilters[c].SetCoefficients(EqFilterType.HighPass, sr, highXover, 0.707, 0);
                }

                _builtLowXover = lowXover;
                _builtHighXover = highXover;
            }

            var lowThreshold = _params.Get("lowThreshold", -18f);
            var lowRatio = Math.Max(1f, _params.Get("lowRatio", 3f));
            var midThreshold = _params.Get("midThreshold", -18f);
            var midRatio = Math.Max(1f, _params.Get("midRatio", 3f));
            var highThreshold = _params.Get("highThreshold", -18f);
            var highRatio = Math.Max(1f, _params.Get("highRatio", 3f));

            var attackCoeff = (float)Math.Exp(-1.0 / (sr * AttackMs / 1000.0));
            var releaseCoeff = (float)Math.Exp(-1.0 / (sr * ReleaseMs / 1000.0));

            Span<float> lowBand = stackalloc float[channels];
            Span<float> midBand = stackalloc float[channels];
            Span<float> highBand = stackalloc float[channels];

            for (var i = 0; i < read; i += channels)
            {
                var lowPeak = 0f;
                var midPeak = 0f;
                var highPeak = 0f;

                for (var c = 0; c < channels; c++)
                {
                    var x = buffer[offset + i + c];
                    var low = _lowFilters[c].Process(x);
                    var high = _highFilters[c].Process(x);
                    var mid = x - low - high;

                    lowBand[c] = low;
                    midBand[c] = mid;
                    highBand[c] = high;

                    lowPeak = Math.Max(lowPeak, Math.Abs(low));
                    midPeak = Math.Max(midPeak, Math.Abs(mid));
                    highPeak = Math.Max(highPeak, Math.Abs(high));
                }

                _lowGainDb = UpdateGain(_lowGainDb, lowPeak, lowThreshold, lowRatio, attackCoeff, releaseCoeff);
                _midGainDb = UpdateGain(_midGainDb, midPeak, midThreshold, midRatio, attackCoeff, releaseCoeff);
                _highGainDb = UpdateGain(_highGainDb, highPeak, highThreshold, highRatio, attackCoeff, releaseCoeff);

                var lowGain = (float)Math.Pow(10, _lowGainDb / 20.0);
                var midGain = (float)Math.Pow(10, _midGainDb / 20.0);
                var highGain = (float)Math.Pow(10, _highGainDb / 20.0);

                for (var c = 0; c < channels; c++)
                {
                    var sum = (lowBand[c] * lowGain) + (midBand[c] * midGain) + (highBand[c] * highGain);
                    buffer[offset + i + c] = Math.Clamp(sum, -1f, 1f);
                }
            }

            return read;
        }

        private static float UpdateGain(float currentDb, float peak, float threshold, float ratio, float attackCoeff, float releaseCoeff)
        {
            var levelDb = peak > 1e-7f ? 20f * (float)Math.Log10(peak) : -120f;
            var targetDb = levelDb > threshold
                ? threshold + ((levelDb - threshold) / ratio) - levelDb
                : 0f;

            return targetDb < currentDb
                ? (attackCoeff * currentDb) + ((1 - attackCoeff) * targetDb)
                : (releaseCoeff * currentDb) + ((1 - releaseCoeff) * targetDb);
        }
    }
}
