using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

public sealed class CompressorEffect : IAudioEffect
{
    public string Name => "Compressor";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("threshold", -18f);  // dBFS
        p.Set("ratio", 4f);        // 4:1
        p.Set("attack", 10f);      // ms
        p.Set("release", 100f);    // ms
        p.Set("makeup", 0f);       // dB gain
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new CompressorSampleProvider(source, Parameters);

    private sealed class CompressorSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;
        private float _gainDb;  // current gain in dB

        public WaveFormat WaveFormat => _source.WaveFormat;

        public CompressorSampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
            _gainDb = 0f;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read == 0)
            {
                return 0;
            }

            var sr = _source.WaveFormat.SampleRate;
            var channels = _source.WaveFormat.Channels;
            var threshold = _params.Get("threshold", -18f);
            var ratio = Math.Max(1f, _params.Get("ratio", 4f));
            var attackMs = Math.Max(0.1f, _params.Get("attack", 10f));
            var releaseMs = Math.Max(1f, _params.Get("release", 100f));
            var makeup = _params.Get("makeup", 0f);

            var attackCoeff = (float)Math.Exp(-1.0 / (sr * attackMs / 1000.0));
            var releaseCoeff = (float)Math.Exp(-1.0 / (sr * releaseMs / 1000.0));

            for (var i = 0; i < read; i += channels)
            {
                // Per-frame peak detector; cheap stand-in for RMS as in legacy.
                var peak = 0f;
                for (var c = 0; c < channels; c++)
                {
                    peak = Math.Max(peak, Math.Abs(buffer[offset + i + c]));
                }

                var levelDb = peak > 1e-7f ? 20f * (float)Math.Log10(peak) : -120f;

                var targetGainDb = levelDb > threshold
                    ? threshold + ((levelDb - threshold) / ratio) - levelDb
                    : 0f;

                // Smooth with attack/release.
                _gainDb = targetGainDb < _gainDb
                    ? (attackCoeff * _gainDb) + ((1 - attackCoeff) * targetGainDb)
                    : (releaseCoeff * _gainDb) + ((1 - releaseCoeff) * targetGainDb);

                var gain = (float)Math.Pow(10, (_gainDb + makeup) / 20.0);
                for (var c = 0; c < channels; c++)
                {
                    buffer[offset + i + c] = Math.Clamp(buffer[offset + i + c] * gain, -1f, 1f);
                }
            }

            return read;
        }
    }
}
