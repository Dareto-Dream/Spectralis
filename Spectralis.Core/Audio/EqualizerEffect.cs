using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace Spectralis.Core.Audio
{
    public class EqualizerBand
    {
        public float Frequency { get; set; }
        public float Gain { get; set; }
        public float Bandwidth { get; set; } = 0.8f;
        public string Label { get; set; } = string.Empty;
    }

    public class EqualizerEffect : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly BiQuadFilter[] _filters;
        private readonly EqualizerBand[] _bands;
        private bool _enabled = true;

        public static readonly float[] DefaultFrequencies = { 32f, 64f, 125f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f };

        public WaveFormat WaveFormat => _source.WaveFormat;
        public bool Enabled { get => _enabled; set => _enabled = value; }
        public EqualizerBand[] Bands => _bands;

        public EqualizerEffect(ISampleProvider source)
        {
            _source = source;
            _bands = new EqualizerBand[DefaultFrequencies.Length];
            _filters = new BiQuadFilter[DefaultFrequencies.Length * source.WaveFormat.Channels];

            for (int i = 0; i < DefaultFrequencies.Length; i++)
            {
                _bands[i] = new EqualizerBand
                {
                    Frequency = DefaultFrequencies[i],
                    Gain = 0f,
                    Label = FormatFrequency(DefaultFrequencies[i])
                };
                CreateFilters(i);
            }
        }

        private void CreateFilters(int bandIndex)
        {
            var band = _bands[bandIndex];
            int channels = _source.WaveFormat.Channels;
            int sampleRate = _source.WaveFormat.SampleRate;

            for (int ch = 0; ch < channels; ch++)
            {
                _filters[bandIndex * channels + ch] = BiQuadFilter.PeakingEQ(
                    sampleRate, band.Frequency, band.Bandwidth, band.Gain);
            }
        }

        public void SetBandGain(int bandIndex, float gainDb)
        {
            if (bandIndex < 0 || bandIndex >= _bands.Length) return;
            _bands[bandIndex].Gain = gainDb;
            CreateFilters(bandIndex);
        }

        public void Reset()
        {
            for (int i = 0; i < _bands.Length; i++)
            {
                _bands[i].Gain = 0f;
                CreateFilters(i);
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            if (!_enabled) return read;

            int channels = _source.WaveFormat.Channels;
            for (int i = offset; i < offset + read; i++)
            {
                int ch = (i - offset) % channels;
                float sample = buffer[i];
                for (int b = 0; b < _bands.Length; b++)
                {
                    if (Math.Abs(_bands[b].Gain) > 0.01f)
                        sample = _filters[b * channels + ch].Transform(sample);
                }
                buffer[i] = Math.Clamp(sample, -1f, 1f);
            }
            return read;
        }

        private static string FormatFrequency(float hz) =>
            hz >= 1000 ? $"{hz / 1000:0.#}k" : $"{hz:0}";
    }
}
