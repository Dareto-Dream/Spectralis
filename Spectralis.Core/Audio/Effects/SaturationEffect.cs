using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Tape/tube-style warmth: soft-clip harmonic coloring via a tanh waveshaper,
/// with a post tone filter and dry/wet mix.
/// </summary>
public sealed class SaturationEffect : IAudioEffect
{
    public string Name => "Saturation";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("drive", 3f);    // 1-10, higher = more harmonic coloring
        p.Set("tone", 0.6f);   // 0=dark, 1=bright
        p.Set("mix", 1f);      // 0=dry, 1=fully saturated
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new SaturationSampleProvider(source, Parameters);

    private sealed class SaturationSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;
        private float[] _toneState = [];

        public WaveFormat WaveFormat => _source.WaveFormat;

        public SaturationSampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
            _toneState = new float[Math.Max(1, source.WaveFormat.Channels)];
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read == 0)
            {
                return 0;
            }

            var channels = Math.Max(1, _source.WaveFormat.Channels);
            var drive = Math.Max(1f, _params.Get("drive", 3f));
            var tone = Math.Clamp(_params.Get("tone", 0.6f), 0f, 1f);
            var mix = Math.Clamp(_params.Get("mix", 1f), 0f, 1f);

            var normalizer = (float)Math.Tanh(drive);
            var cutoff = 800f + (tone * 17200f);
            var alpha = (float)(1.0 - Math.Exp(-2.0 * Math.PI * cutoff / _source.WaveFormat.SampleRate));

            for (var i = 0; i < read; i += channels)
            {
                for (var c = 0; c < channels; c++)
                {
                    var x = buffer[offset + i + c];
                    var shaped = (float)Math.Tanh(x * drive) / normalizer;

                    _toneState[c] += alpha * (shaped - _toneState[c]);
                    var toned = _toneState[c];

                    buffer[offset + i + c] = Math.Clamp((x * (1f - mix)) + (toned * mix), -1f, 1f);
                }
            }

            return read;
        }
    }
}
