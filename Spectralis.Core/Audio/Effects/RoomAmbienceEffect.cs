using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Lightweight room/ambience simulation: a handful of discrete early-reflection
/// taps (no feedback tail), modeling the first bounces off nearby surfaces rather
/// than a full decaying reverb. Much cheaper than <see cref="ConvolutionReverbEffect"/>
/// and voiced differently than the comb/allpass <see cref="ReverbEffect"/>.
/// </summary>
public sealed class RoomAmbienceEffect : IAudioEffect
{
    public string Name => "Room Ambience";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("size", 0.4f);     // 0-1, tap spacing / perceived room size
        p.Set("damping", 0.5f);  // 0-1, high-frequency absorption
        p.Set("mix", 0.25f);     // 0-1
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new AmbienceSampleProvider(source, Parameters);

    private sealed class AmbienceSampleProvider : ISampleProvider
    {
        // Relative tap positions (0-1 of the size-scaled span) and relative gains,
        // loosely modeling a handful of early reflections off nearby surfaces.
        private static readonly float[] TapPositions = [0.06f, 0.11f, 0.17f, 0.24f, 0.33f, 0.45f, 0.6f, 0.8f];
        private static readonly float[] TapGains = [0.5f, 0.42f, 0.38f, 0.3f, 0.26f, 0.2f, 0.15f, 0.1f];

        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;
        private readonly int _sampleRate;
        private readonly float[] _buffer;
        private readonly int _bufferLength;
        private int _writePos;
        private float _lpState;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public AmbienceSampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
            _sampleRate = source.WaveFormat.SampleRate;
            _bufferLength = (int)(0.09 * _sampleRate) + 4; // up to ~90ms max span
            _buffer = new float[_bufferLength];
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read == 0)
            {
                return 0;
            }

            var channels = Math.Max(1, _source.WaveFormat.Channels);
            var size = Math.Clamp(_params.Get("size", 0.4f), 0f, 1f);
            var damping = Math.Clamp(_params.Get("damping", 0.5f), 0f, 1f);
            var mix = Math.Clamp(_params.Get("mix", 0.25f), 0f, 1f);

            var spanMs = 10f + (size * 70f);
            var spanSamples = spanMs / 1000f * _sampleRate;
            var cutoff = 12000f - (damping * 9000f);
            var alpha = (float)(1.0 - Math.Exp(-2.0 * Math.PI * cutoff / _sampleRate));

            for (var i = 0; i < read; i += channels)
            {
                var mono = 0f;
                for (var c = 0; c < channels; c++)
                {
                    mono += buffer[offset + i + c];
                }

                mono /= channels;
                _buffer[_writePos] = mono;

                var wet = 0f;
                for (var t = 0; t < TapPositions.Length; t++)
                {
                    var delay = (int)(TapPositions[t] * spanSamples);
                    var pos = _writePos - delay;
                    if (pos < 0) pos += _bufferLength;
                    wet += _buffer[pos] * TapGains[t];
                }

                _lpState += alpha * (wet - _lpState);

                for (var c = 0; c < channels; c++)
                {
                    var dry = buffer[offset + i + c];
                    buffer[offset + i + c] = Math.Clamp((dry * (1f - (mix * 0.5f))) + (_lpState * mix), -1f, 1f);
                }

                _writePos = (_writePos + 1) % _bufferLength;
            }

            return read;
        }
    }
}
