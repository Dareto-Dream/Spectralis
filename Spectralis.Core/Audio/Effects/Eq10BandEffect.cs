using NAudio.Dsp;
using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

public sealed class Eq10BandEffect : IAudioEffect
{
    public static readonly float[] BandFrequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
    private const float Q = 1.41f;  // ~1 octave bandwidth

    public string Name => "10-Band EQ";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        for (var i = 0; i < BandFrequencies.Length; i++)
        {
            p.Set($"band{i}", 0f);
        }

        p.Set("preamp", 0f);
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new EqSampleProvider(source, Parameters);

    private sealed class EqSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;
        private BiQuadFilter[][]? _filters;  // [channel][band]
        private readonly int _sampleRate;
        private readonly int _channels;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public EqSampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
            _sampleRate = source.WaveFormat.SampleRate;
            _channels = source.WaveFormat.Channels;
            RebuildFilters();
        }

        private void RebuildFilters()
        {
            _filters = new BiQuadFilter[_channels][];
            for (var c = 0; c < _channels; c++)
            {
                _filters[c] = new BiQuadFilter[BandFrequencies.Length];
                for (var b = 0; b < BandFrequencies.Length; b++)
                {
                    var gain = _params.Get($"band{b}", 0f);
                    _filters[c][b] = BiQuadFilter.PeakingEQ(_sampleRate, BandFrequencies[b], Q, gain);
                }
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (_filters is null)
            {
                return read;
            }

            var preamp = (float)Math.Pow(10, _params.Get("preamp", 0f) / 20.0);

            for (var i = 0; i < read; i += _channels)
            {
                for (var c = 0; c < _channels; c++)
                {
                    var s = buffer[offset + i + c] * preamp;
                    for (var b = 0; b < BandFrequencies.Length; b++)
                    {
                        s = _filters[c][b].Transform(s);
                    }

                    buffer[offset + i + c] = Math.Clamp(s, -1f, 1f);
                }
            }

            return read;
        }
    }
}
