using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Harder-edged overdrive/distortion: hard-clip waveshaper (more aggressive than
/// <see cref="SaturationEffect"/>'s tanh curve), with a post tone filter and mix.
/// </summary>
public sealed class DistortionEffect : IAudioEffect
{
    public string Name => "Distortion";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("drive", 8f);    // 1-30
        p.Set("tone", 0.5f);   // 0=dark, 1=bright
        p.Set("mix", 1f);      // 0=dry, 1=fully distorted
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new DistortionSampleProvider(source, Parameters);

    private sealed class DistortionSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;
        private float[] _toneState = [];

        public WaveFormat WaveFormat => _source.WaveFormat;

        public DistortionSampleProvider(ISampleProvider source, EffectParameters parameters)
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
            var drive = Math.Max(1f, _params.Get("drive", 8f));
            var tone = Math.Clamp(_params.Get("tone", 0.5f), 0f, 1f);
            var mix = Math.Clamp(_params.Get("mix", 1f), 0f, 1f);

            // Hard clip is louder than tanh at the same drive; compensate so the
            // knob feels roughly level-matched across its range.
            var makeup = 1f / (float)Math.Sqrt(drive);
            var cutoff = 800f + (tone * 17200f);
            var alpha = (float)(1.0 - Math.Exp(-2.0 * Math.PI * cutoff / _source.WaveFormat.SampleRate));

            for (var i = 0; i < read; i += channels)
            {
                for (var c = 0; c < channels; c++)
                {
                    var x = buffer[offset + i + c];
                    var shaped = Math.Clamp(x * drive, -1f, 1f) * makeup;

                    _toneState[c] += alpha * (shaped - _toneState[c]);
                    var toned = _toneState[c];

                    buffer[offset + i + c] = Math.Clamp((x * (1f - mix)) + (toned * mix), -1f, 1f);
                }
            }

            return read;
        }
    }
}
