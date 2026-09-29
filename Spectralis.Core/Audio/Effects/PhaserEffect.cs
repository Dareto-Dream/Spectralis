using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Swept all-pass effect: a cascade of first-order all-pass stages whose corner
/// frequency is modulated by a shared LFO, producing the classic phaser "swoosh"
/// (notches move through the spectrum rather than the comb-filter teeth a flanger produces).
/// </summary>
public sealed class PhaserEffect : IAudioEffect
{
    public string Name => "Phaser";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("rate", 0.3f);      // Hz
        p.Set("depth", 0.7f);     // 0-1
        p.Set("feedback", 0.3f);  // 0-0.95
        p.Set("mix", 0.5f);       // 0-1
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new PhaserSampleProvider(source, Parameters);

    private sealed class PhaserSampleProvider : ISampleProvider
    {
        private const int Stages = 4;
        private const float MinHz = 200f;
        private const float MaxHz = 2000f;

        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;
        private readonly int _sampleRate;
        private readonly float[][] _stageState;
        private readonly float[] _feedbackState;
        private double _phase;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public PhaserSampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
            _sampleRate = source.WaveFormat.SampleRate;
            var channels = Math.Max(1, source.WaveFormat.Channels);
            _stageState = new float[channels][];
            for (var c = 0; c < channels; c++)
            {
                _stageState[c] = new float[Stages];
            }

            _feedbackState = new float[channels];
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read == 0)
            {
                return 0;
            }

            var channels = _stageState.Length;
            var rate = Math.Max(0.01f, _params.Get("rate", 0.3f));
            var depth = Math.Clamp(_params.Get("depth", 0.7f), 0f, 1f);
            var feedback = Math.Clamp(_params.Get("feedback", 0.3f), 0f, 0.95f);
            var mix = Math.Clamp(_params.Get("mix", 0.5f), 0f, 1f);
            var phaseStep = 2.0 * Math.PI * rate / _sampleRate;

            for (var i = 0; i < read; i += channels)
            {
                var lfo = (float)((Math.Sin(_phase) + 1.0) / 2.0);
                var sweepHz = MinHz + (depth * (MaxHz - MinHz) * lfo);
                var tanArg = Math.Tan(Math.PI * sweepHz / _sampleRate);
                var a = (float)((tanArg - 1.0) / (tanArg + 1.0));

                for (var c = 0; c < channels; c++)
                {
                    var dry = buffer[offset + i + c];
                    var x = dry + (_feedbackState[c] * feedback);

                    for (var s = 0; s < Stages; s++)
                    {
                        var state = _stageState[c][s];
                        var y = (a * x) + state;
                        _stageState[c][s] = x - (a * y);
                        x = y;
                    }

                    _feedbackState[c] = x;
                    buffer[offset + i + c] = Math.Clamp((dry * (1f - mix)) + (x * mix), -1f, 1f);
                }

                _phase += phaseStep;
                if (_phase > 2.0 * Math.PI)
                {
                    _phase -= 2.0 * Math.PI;
                }
            }

            return read;
        }
    }
}
