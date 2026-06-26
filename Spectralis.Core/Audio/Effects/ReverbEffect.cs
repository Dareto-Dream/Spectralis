using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

public sealed class ReverbEffect : IAudioEffect
{
    public string Name => "Reverb";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("roomSize", 0.5f);  // 0–1
        p.Set("damping", 0.5f);   // 0–1
        p.Set("wet", 0.3f);       // 0–1
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new ReverbSampleProvider(source, Parameters);

    private sealed class ReverbSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;

        // Comb filter delays (mono, at 44100 Hz).
        private static readonly int[] CombDelays = [1116, 1188, 1277, 1356];
        private static readonly int[] AllpassDelays = [225, 556];

        private float[][] _combBuffers = [];
        private int[] _combPositions = [];
        private float[] _combFiltered = [];
        private float[][] _apBuffers = [];
        private int[] _apPositions = [];

        public WaveFormat WaveFormat => _source.WaveFormat;

        public ReverbSampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
            InitBuffers(source.WaveFormat.SampleRate);
        }

        private void InitBuffers(int sampleRate)
        {
            var scale = sampleRate / 44100.0;

            _combBuffers = CombDelays.Select(d => new float[(int)((d * scale) + 1)]).ToArray();
            _combPositions = new int[CombDelays.Length];
            _combFiltered = new float[CombDelays.Length];

            _apBuffers = AllpassDelays.Select(d => new float[(int)((d * scale) + 1)]).ToArray();
            _apPositions = new int[AllpassDelays.Length];
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read == 0)
            {
                return 0;
            }

            var channels = _source.WaveFormat.Channels;
            var roomSize = _params.Get("roomSize", 0.5f);
            var damping = _params.Get("damping", 0.5f);
            var wet = _params.Get("wet", 0.3f);
            var dry = 1f - (wet * 0.5f);

            var feedback = 0.5f + (roomSize * 0.28f);  // ~0.5–0.78
            var damp = damping * 0.4f;

            for (var i = 0; i < read; i += channels)
            {
                // Mix to mono input, scaled down to avoid feedback clipping.
                var input = 0f;
                for (var c = 0; c < channels; c++)
                {
                    input += buffer[offset + i + c];
                }

                input /= channels;
                input *= 0.015f;

                // Parallel comb filters.
                var output = 0f;
                for (var cb = 0; cb < _combBuffers.Length; cb++)
                {
                    var buf = _combBuffers[cb];
                    var pos = _combPositions[cb];
                    var combOut = buf[pos];
                    _combFiltered[cb] = (combOut * (1f - damp)) + (_combFiltered[cb] * damp);
                    buf[pos] = input + (_combFiltered[cb] * feedback);
                    _combPositions[cb] = (pos + 1) % buf.Length;
                    output += combOut;
                }

                // Series allpass filters.
                for (var ap = 0; ap < _apBuffers.Length; ap++)
                {
                    var buf = _apBuffers[ap];
                    var pos = _apPositions[ap];
                    var apOut = buf[pos] - output;
                    buf[pos] = output + (buf[pos] * 0.5f);
                    _apPositions[ap] = (pos + 1) % buf.Length;
                    output = apOut;
                }

                for (var c = 0; c < channels; c++)
                {
                    var original = buffer[offset + i + c];
                    buffer[offset + i + c] = Math.Clamp((original * dry) + (output * wet), -1f, 1f);
                }
            }

            return read;
        }
    }
}
