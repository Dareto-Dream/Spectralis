using System;
using NAudio.Wave;

namespace Spectralis.Core.Audio
{
    public class AudioSampleTap : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly WaveformBuffer _waveformBuffer;
        private readonly FftProcessor _fftProcessor;

        public event EventHandler<float[]>? SamplesReady;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public AudioSampleTap(ISampleProvider source, WaveformBuffer waveformBuffer, FftProcessor fftProcessor)
        {
            _source = source;
            _waveformBuffer = waveformBuffer;
            _fftProcessor = fftProcessor;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);

            int channels = WaveFormat.Channels;
            int samples = read / channels;
            var mono = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float sum = 0;
                for (int ch = 0; ch < channels; ch++)
                    sum += buffer[offset + i * channels + ch];
                mono[i] = sum / channels;
                _fftProcessor.Add(mono[i]);
            }

            _waveformBuffer.Write(mono, 0, mono.Length);
            SamplesReady?.Invoke(this, mono);

            return read;
        }
    }
}
