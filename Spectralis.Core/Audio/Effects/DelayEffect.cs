using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Basic feedback delay/echo, in free-running milliseconds (not tempo-synced).
/// Often more used in practice than reverb alone.
/// </summary>
public sealed class DelayEffect : IAudioEffect
{
    public string Name => "Delay";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("time", 350f);      // ms
        p.Set("feedback", 0.35f); // 0-0.95
        p.Set("mix", 0.3f);       // 0-1
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new DelaySampleProvider(source, Parameters);

    private sealed class DelaySampleProvider : ISampleProvider
    {
        private const float MaxDelayMs = 2000f;

        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;
        private readonly float[][] _lines;
        private readonly int _bufferLength;
        private int _writePos;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public DelaySampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
            var channels = Math.Max(1, source.WaveFormat.Channels);
            _bufferLength = (int)(MaxDelayMs / 1000.0 * source.WaveFormat.SampleRate) + 4;
            _lines = new float[channels][];
            for (var c = 0; c < channels; c++)
            {
                _lines[c] = new float[_bufferLength];
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read == 0)
            {
                return 0;
            }

            var channels = _lines.Length;
            var timeMs = Math.Clamp(_params.Get("time", 350f), 1f, MaxDelayMs);
            var feedback = Math.Clamp(_params.Get("feedback", 0.35f), 0f, 0.95f);
            var mix = Math.Clamp(_params.Get("mix", 0.3f), 0f, 1f);
            var delaySamples = Math.Min(_bufferLength - 1, (int)(timeMs / 1000.0 * _source.WaveFormat.SampleRate));

            for (var i = 0; i < read; i += channels)
            {
                var readPos = _writePos - delaySamples;
                if (readPos < 0)
                {
                    readPos += _bufferLength;
                }

                for (var c = 0; c < channels; c++)
                {
                    var delayed = _lines[c][readPos];
                    var dry = buffer[offset + i + c];

                    _lines[c][_writePos] = Math.Clamp(dry + (delayed * feedback), -1f, 1f);
                    buffer[offset + i + c] = Math.Clamp((dry * (1f - mix)) + (delayed * mix), -1f, 1f);
                }

                _writePos = (_writePos + 1) % _bufferLength;
            }

            return read;
        }
    }
}
