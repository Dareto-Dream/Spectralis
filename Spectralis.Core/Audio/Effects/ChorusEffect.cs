using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Modulated delay for width and movement: a short delay line per channel whose
/// length is swept by a low-frequency oscillator, mixed back with the dry signal.
/// Channels are offset 90° apart so the modulation isn't perfectly correlated.
/// </summary>
public sealed class ChorusEffect : IAudioEffect
{
    public string Name => "Chorus";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("rate", 0.8f);   // Hz
        p.Set("depth", 0.5f);  // 0-1
        p.Set("mix", 0.5f);    // 0-1
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new ChorusSampleProvider(source, Parameters);

    private sealed class ChorusSampleProvider : ISampleProvider
    {
        private const float BaseDelayMs = 7f;
        private const float MaxDepthMs = 8f;

        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;
        private readonly int _sampleRate;
        private readonly float[][] _delayLines;
        private readonly int _bufferLength;
        private int _writePos;
        private double _phase;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public ChorusSampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
            _sampleRate = source.WaveFormat.SampleRate;
            var channels = Math.Max(1, source.WaveFormat.Channels);
            _bufferLength = (int)((BaseDelayMs + MaxDepthMs + 2) / 1000.0 * _sampleRate) + 4;
            _delayLines = new float[channels][];
            for (var c = 0; c < channels; c++)
            {
                _delayLines[c] = new float[_bufferLength];
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read == 0)
            {
                return 0;
            }

            var channels = _delayLines.Length;
            var rate = Math.Max(0.01f, _params.Get("rate", 0.8f));
            var depth = Math.Clamp(_params.Get("depth", 0.5f), 0f, 1f);
            var mix = Math.Clamp(_params.Get("mix", 0.5f), 0f, 1f);
            var phaseStep = 2.0 * Math.PI * rate / _sampleRate;

            for (var i = 0; i < read; i += channels)
            {
                for (var c = 0; c < channels; c++)
                {
                    var channelPhase = _phase + (c * Math.PI / 2.0);
                    var delayMs = BaseDelayMs + (depth * MaxDepthMs * (float)((Math.Sin(channelPhase) + 1.0) / 2.0));
                    var delaySamples = delayMs / 1000.0 * _sampleRate;

                    var readPos = _writePos - delaySamples;
                    while (readPos < 0)
                    {
                        readPos += _bufferLength;
                    }

                    var delayed = ReadInterpolated(_delayLines[c], readPos);

                    var dry = buffer[offset + i + c];
                    _delayLines[c][_writePos] = dry;
                    buffer[offset + i + c] = Math.Clamp((dry * (1f - mix)) + (delayed * mix), -1f, 1f);
                }

                _writePos = (_writePos + 1) % _bufferLength;
                _phase += phaseStep;
                if (_phase > 2.0 * Math.PI)
                {
                    _phase -= 2.0 * Math.PI;
                }
            }

            return read;
        }

        private float ReadInterpolated(float[] line, double pos)
        {
            var i0 = (int)pos;
            var frac = (float)(pos - i0);
            var i1 = (i0 + 1) % _bufferLength;
            i0 %= _bufferLength;
            return line[i0] + ((line[i1] - line[i0]) * frac);
        }
    }
}
